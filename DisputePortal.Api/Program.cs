using DisputePortal.Platform.Infrastructure;
using DisputePortal.Api.Extensions;
using DisputePortal.Api.Repository.Data;
using DisputePortal.Api.Realtime;
using Microsoft.AspNetCore.Http.Connections;
using Serilog;
using Serilog.Events;
using DisputePortal.Api.Interfaces.Data;

var migrateOnly = args.Contains("--initialize", StringComparer.Ordinal);
var builder = WebApplication.CreateBuilder(args.Where(arg => arg != "--initialize").ToArray());
builder.Configuration.AddPortalDevelopmentSettings(builder.Environment, args.Where(arg => arg != "--initialize").ToArray());
builder.Configuration.AddJsonFile(
    Path.Combine(builder.Environment.ContentRootPath, "Configuration", "push-providers.json"),
    optional: false,
    reloadOnChange: true);
if (string.IsNullOrWhiteSpace(builder.Configuration["MessageTemplates:Directory"]))
{
    builder.Configuration["MessageTemplates:Directory"] = Path.Combine(builder.Environment.ContentRootPath, "Templates", "Messages");
}

if (string.IsNullOrWhiteSpace(builder.Configuration["TransactionPresentation:File"]))
{
    builder.Configuration["TransactionPresentation:File"] = Path.Combine(
        builder.Environment.ContentRootPath,
        "Templates",
        "Transactions",
        "presentation.json");
}

migrateOnly = migrateOnly || builder.Configuration.GetValue("Database:MigrationOnly", false);
var logOptions = builder.Configuration.GetRequiredSection("PortalLogging").Get<DisputePortal.Platform.Infrastructure.Logging.PortalLogOptions>() ?? throw new InvalidOperationException("Application logging configuration is required.");
logOptions.ValidateLogRetentionOptions();
builder.Services.AddSingleton<DisputePortal.Platform.Infrastructure.Logging.RotatingApplicationLog>(_ => new(
    logOptions,
    builder.Environment.ContentRootPath,
    builder.Environment.ApplicationName,
    builder.Environment.EnvironmentName));
builder.Host.UseSerilog((_, services, loggerConfiguration) =>
{
    loggerConfiguration.MinimumLevel.Information().MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning).Enrich.FromLogContext().WriteTo.Sink(services.GetRequiredService<DisputePortal.Platform.Infrastructure.Logging.RotatingApplicationLog>());
    if (logOptions.ConsoleEnabled)
    {
        loggerConfiguration.WriteTo.Console(services.GetRequiredService<DisputePortal.Platform.Infrastructure.Logging.RotatingApplicationLog>());
    }
});
DisputePortal.Api.Platform.PlatformExtensions.AddFinancialPlatform(builder);
builder.Configuration.AddJsonFile(
    Path.Combine(builder.Environment.ContentRootPath, "Configuration", "portal-reference.json"),
    optional: false,
    reloadOnChange: true).AddEnvironmentVariables();
var serviceTargets = builder.Configuration.GetSection("Operations:ServiceTargets").Get<DisputePortal.Api.Models.Domain.CaseServicePolicy>() ?? new();
serviceTargets.ValidateCaseServiceRules();
builder.Services.AddSingleton(serviceTargets);
var sessionPolicy = builder.Configuration.GetSection("Authentication:Session").Get<DisputePortal.Api.Models.Domain.SessionPolicy>() ?? new();
sessionPolicy.ValidateSessionPolicy();
builder.Services.AddSingleton(sessionPolicy);
builder.Services.AddHostedService<DisputePortal.Api.Service.BackgroundJobs.AssignmentWorker>();
builder.Services.AddHostedService<DisputePortal.Api.Service.BackgroundJobs.AutomaticRefundWorker>();
builder.Services.AddPortalApplication();
var databaseConfiguration = DatabaseConfiguration.ResolveDatabaseConfiguration(builder.Configuration);
builder.Services.AddPortalInfrastructure(builder.Configuration, databaseConfiguration);
builder.Services.AddPortalApi(builder.Configuration, builder.Environment);
DisputePortal.Api.Configuration.ProductionSecurity.ValidateProductionSecurity(
    builder.Configuration,
    builder.Environment,
    databaseConfiguration);
builder.Services.AddHttpClient<DisputePortal.Api.Service.Security.PasswordSafety>((services, client) =>
{
    var runtime = services.GetRequiredService<DisputePortal.Api.Configuration.ApiRuntimeOptions>();
    client.Timeout = TimeSpan.FromSeconds(runtime.PasswordCheckTimeoutSeconds);
    if (builder.Configuration.GetValue<bool>("PasswordSecurity:CheckBreaches"))
    {
        client.BaseAddress = new Uri(runtime.PasswordCheckEndpoint);
    }
}).ConfigurePrimaryHttpMessageHandler(() => DisputePortal.Platform.Infrastructure.TransportSecurity.HttpHandler());
await using var app = builder.Build();
if (app.Services.GetService<ServiceOptions>() is { } financialOptions)
{
    financialOptions.Subscriptions = app.Services
        .GetServices<DisputePortal.Platform.Infrastructure.Interfaces.IDomainEventConsumer>()
        .SelectMany(consumer => consumer.Subscriptions)
        .Distinct(StringComparer.Ordinal)
        .ToArray();
    EventRouting.ValidateSubscriptions(financialOptions);
}
var logger = app.Logger;
var startupToken = app.Lifetime.ApplicationStopping;
logger.LogInformation(
    "Runtime collector ServerGC {ServerGC}, processors {ProcessorCount}",
    System.Runtime.GCSettings.IsServerGC,
    Environment.ProcessorCount);
using (logger.BeginScope(new Dictionary<string, object> { ["Operation"] = "Database.Initialize" }))
using (var scope = app.Services.CreateScope())
{
    try
    {
        if (app.Environment.IsDevelopment()
            && (app.Configuration.GetValue<bool>("Database:AutoCreateDatabase")
            || databaseConfiguration.Provider == DatabaseProvider.SqlServer
            && app.Configuration.GetValue<bool>("Database:AutoCreateSqlServerDatabase")))
        {
            var database = scope.ServiceProvider.GetRequiredService<IDatabaseConnectionFactory>();
            await database.EnsureDatabaseExists(startupToken);
        }

        var migrator = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();
        if (migrateOnly || app.Environment.IsDevelopment() && app.Configuration.GetValue("Database:ApplyMigrationsOnStartup", true))
        {
            await migrator.InitializeDatabase(startupToken);
        }
        else
        {
            await migrator.ValidateSchema(startupToken);
        }

        if (!migrateOnly)
        {
            var created = await scope.ServiceProvider.GetRequiredService<DisputePortal.Api.Repository.DevelopmentUserInitializer>().InitializeConfiguredDevelopmentUsers(
                app.Environment,
                app.Configuration,
                startupToken);
            if (created > 0)
            {
                logger.LogInformation("Configured development identities initialized: {Rows}", created);
            }
        }

        logger.LogInformation("Database is ready.");
    }
    catch (Exception exception)
    {
        logger.LogError(
            exception,
            "Database initialization failed ({ExceptionType}). Check database availability and migration configuration.",
            exception.GetType().Name);
        throw;
    }
}

if (migrateOnly)
{
    logger.LogInformation("Migration-only execution completed; HTTP and background workers were not started.");
    return;
}

app.UsePortalHttpPipeline();
app.MapHealthChecks("/health/live", new() { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new() { Predicate = check => check.Tags.Contains("ready") });
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Dispute Portal API v1");
        var originalIndex = options.IndexStream;
        options.IndexStream = () =>
        {
            using var stream = originalIndex();
            using var reader = new StreamReader(stream);
            var nonce = app.Services.GetRequiredService<IHttpContextAccessor>().HttpContext?.Items["SwaggerNonce"] as string;
            return new MemoryStream(System.Text.Encoding.UTF8.GetBytes(reader.ReadToEnd().Replace(
                "<script",
                $"<script nonce=\"{nonce}\"",
                StringComparison.Ordinal)));
        };
    });
}

if (app.Configuration.GetValue<bool>("Platform:Enabled"))
{
    app.MapHttpEventDelivery(DisputePortal.Platform.Infrastructure.ServiceSecurity.InternalServiceScheme);
}

app.MapControllers().RequirePortalAntiforgery(app.Configuration);
var apiRuntime = app.Services.GetRequiredService<DisputePortal.Api.Configuration.ApiRuntimeOptions>();
app.MapHub<PortalRealtimeHub>(
    DisputePortal.Api.Realtime.PortalRealtimeHub.EndpointPath,
    options =>
{
    options.Transports = HttpTransportType.WebSockets;
    options.CloseOnAuthenticationExpiration = true;
    options.AllowStatefulReconnects = true;
    options.ApplicationMaxBufferSize = 16 * 1024;
    options.TransportMaxBufferSize = 16 * 1024;
    options.TransportSendTimeout = TimeSpan.FromSeconds(apiRuntime.RealtimeTransportSendSeconds);
    options.WebSockets.CloseTimeout = TimeSpan.FromSeconds(apiRuntime.RealtimeCloseSeconds);
}).RequireAuthorization().RequireRateLimiting("realtime").RequirePortalAntiforgery(app.Configuration);
app.Run();
