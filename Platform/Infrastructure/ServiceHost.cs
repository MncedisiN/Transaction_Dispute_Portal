using DisputePortal.Platform.Infrastructure.Logging;
using Serilog;
using Serilog.Events;
using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using DisputePortal.Platform.Infrastructure.Models;
using DisputePortal.Platform.Infrastructure.Interfaces;

namespace DisputePortal.Platform.Infrastructure;

public static class ServiceHost
{
    public static async Task RunServiceHost<T>(string name, string[] args)
        where T : class, IServiceModule
    {
        PlatformMapping.InitializeDapperMappings();
        var builder = WebApplication.CreateBuilder(args.Where(a => a != "--migrate").ToArray());
        builder.Configuration.AddIntegrationDefaults();
        var privatePath = Environment.GetEnvironmentVariable("PLATFORM_SETTINGS") ?? throw new InvalidOperationException("Set PLATFORM_SETTINGS to the private service settings file.");
        builder.Configuration.AddJsonFile(privatePath, false, true).AddEnvironmentVariables();
        var options = builder.Configuration.GetSection("Platform").Get<ServiceOptions>() ?? throw new InvalidOperationException("Missing service settings.");
        options.ValidateServiceConfiguration();
        if (options.DatabaseProvider != "PostgreSql")
        {
            throw new InvalidOperationException("Financial services require their own PostgreSQL database. Identity and disputes use their separately configured database boundary.");
        }

        if (options.Name != name)
        {
            throw new InvalidOperationException("Service settings belong to a different service.");
        }

        if (!builder.Environment.IsDevelopment())
        {
            TransportSecurity.ValidateServerConfiguration(builder.Configuration);
            TransportSecurity.ValidateServices(options);
        }

        builder.WebHost.ConfigureKestrel(TransportSecurity.ConfigureHttps);
        builder.Services.AddHttpsRedirection(https => https.HttpsPort = builder.Configuration.GetValue<int>("HttpsRedirection:HttpsPort"));
        builder.Services.AddHsts(hsts => hsts.MaxAge = TimeSpan.FromDays(options.HstsMaxAgeDays));
        var logging = builder.Configuration.GetRequiredSection("PortalLogging").Get<PortalLogOptions>() ?? throw new InvalidOperationException("Application logging configuration is required.");
        logging.ValidateLogRetentionOptions();
        builder.Services.AddSingleton<RotatingApplicationLog>(_ => new(
            logging,
            builder.Environment.ContentRootPath,
            builder.Environment.ApplicationName,
            builder.Environment.EnvironmentName));
        builder.Host.UseSerilog((_, services, configuration) =>
        {
            var sink = services.GetRequiredService<RotatingApplicationLog>();
            configuration.MinimumLevel.Information().MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning).Enrich.FromLogContext().WriteTo.Sink(sink);
            if (logging.ConsoleEnabled)
            {
                configuration.WriteTo.Console(sink);
            }
        });
        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<ServiceExceptionHandler>();
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<ServiceDatabase>();
        T.ConfigureServices(builder.Services);
        builder.Services.AddSingleton<T>();
        builder.Services.AddServiceSecurity(options);
        builder.Services.AddServiceHttpClient(builder.Configuration);
        builder.Services.AddSingleton<EventBus>();
        builder.Services.AddSingleton<EventRecovery>();
        builder.Services.AddHostedService<EventWorker>();
        builder.Services.AddHostedService<ModuleWorker>();
        var telemetryEndpoint = TransportSecurity.MonitoringEndpoint(builder.Configuration, !builder.Environment.IsDevelopment());
        builder.Services.AddOpenTelemetry().ConfigureResource(r => r.AddService(name)).WithTracing(tracing =>
        {
            tracing.AddSource(PlatformMetrics.InstrumentationName)
                .AddAspNetCoreInstrumentation(options => options.RecordException = false)
                .AddHttpClientInstrumentation(options => options.RecordException = false)
                .AddProcessor(new TracePrivacyProcessor());
            if (telemetryEndpoint is not null)
            {
                tracing.AddOtlpExporter(exporter => exporter.Endpoint = telemetryEndpoint);
            }
        }).WithMetrics(metrics =>
        {
            metrics.AddMeter(PlatformMetrics.InstrumentationName).AddAspNetCoreInstrumentation().AddHttpClientInstrumentation();
            if (telemetryEndpoint is not null)
            {
                metrics.AddOtlpExporter(exporter => exporter.Endpoint = telemetryEndpoint);
            }
        });
        await using var app = builder.Build();
        var module = app.Services.GetRequiredService<T>();
        options.Subscriptions = app.Services.GetServices<IDomainEventConsumer>().SelectMany(c => c.Subscriptions).Distinct().ToArray();
        EventRouting.ValidateSubscriptions(options);
        var db = app.Services.GetRequiredService<ServiceDatabase>();
        try
        {
            var startupToken = app.Lifetime.ApplicationStopping;
            var migrations = ServiceMigrations.GetServiceMigrations(name, options.DatabaseProvider);
            var applyMigrations = args.Contains("--migrate")
                || (app.Environment.IsDevelopment()
                && builder.Configuration.GetValue("Database:ApplyMigrationsOnStartup", true));
            if (applyMigrations)
            {
                await db.ApplyServiceSchemaMigrations(module.Schema, migrations, startupToken);
                foreach (var initializer in app.Services.GetServices<IServiceInitializer>())
                    await initializer.InitializeServiceData(startupToken);
            }
            else
            {
                await db.ValidateSchema(module.Schema, migrations, startupToken);
            }
        }
        catch (OperationCanceledException) when (app.Lifetime.ApplicationStopping.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error)
        {
            using var scope = app.Logger.BeginScope(new Dictionary<string, object?> { ["Operation"] = "Service.Startup" });
            app.Logger.LogCritical(error, "Service initialization failed; the host will not accept requests.");
            throw;
        }

        if (args.Contains("--migrate"))
        {
            return;
        }

        app.Use(async (context, next) =>
        {
            var correlation = Guid.TryParseExact(context.Request.Headers["X-Correlation-ID"].ToString(), "D", out var supplied) ? supplied.ToString("D") : Guid.NewGuid().ToString("D");
            context.Items["CorrelationId"] = correlation;
            Activity.Current?.SetTag("correlation.id", correlation);
            context.Response.OnStarting(() =>
            {
                context.Response.Headers.CacheControl = "no-store";
                context.Response.Headers["X-Correlation-ID"] = correlation;
                return Task.CompletedTask;
            });
            using var scope = app.Logger.BeginScope(new Dictionary<string, object?>
            {
                ["CorrelationId"] = correlation,
                ["TraceId"] = Activity.Current?.TraceId.ToString(),
                ["Operation"] = "Service.Request"
            });
            await next();
        });
        app.UseExceptionHandler();
        if (!app.Environment.IsDevelopment())
        {
            app.UseHsts();
            app.UseHttpsRedirection();
        }

        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/health/live", () => Results.Ok(new { service = name, status = "live" }));
        app.MapGet(
            "/health/ready",
            async () =>
        {
            await using var c = await db.OpenServiceDatabaseConnection();
            return Results.Ok(new
            {
                service = name,
                status = "ready",
                transport = options.BrokerEnabled ? "rabbitmq" : "http",
                broker = !options.BrokerEnabled ? "disabled" : app.Services.GetRequiredService<EventBus>().IsConnected ? "connected" : "recoverable-outbox"
            });
        });
        app.MapGet(
            "/operations/delivery",
            async (HttpContext context, EventRecovery recovery, CancellationToken ct) =>
        {
            context.User.RequireCaller("identity");
            context.User.RequireAdmin();
            return await recovery.Status(ct);
        }).RequireAuthorization();
        if (options.Subscriptions.Length > 0)
        {
            app.MapPost(
                "/operations/deadletters/replay",
                async (
                    ReplayDeadLetters request,
                    HttpContext context,
                    EventRecovery recovery,
                    CancellationToken ct) =>
            {
                context.User.RequireCaller("identity");
                context.User.RequireAdmin();
                return await recovery.Replay(request, context.User.UserId(), ct);
            }).RequireAuthorization();
        }

        app.MapGet("/metrics", () => Results.Text(PlatformMetrics.Render(), "text/plain"));
        app.MapHttpEventDelivery();
        module.MapServiceEndpoints(app);
        await app.RunAsync();
    }
}
