using DisputePortal.Api.Caching;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Security.Claims;
using System.Threading.RateLimiting;
using DisputePortal.Api.Authorization;
using DisputePortal.Api.Configuration;
using DisputePortal.Api.HealthChecks;
using DisputePortal.Api.Models;
using DisputePortal.Api.Observability;
using DisputePortal.Api.Realtime;
using DisputePortal.Api.Service.Push;
using DisputePortal.Api.Swagger;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using DisputePortal.Api.Interfaces.Push;

namespace DisputePortal.Api.Extensions;

public static class ApiServiceCollectionExtensions
{
    public static IServiceCollection AddPortalApi(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var runtime = ApiRuntimeOptions.LoadRuntimeOptions(configuration);
        services.TryAddSingleton(runtime);
        services.TryAddSingleton(TimeZoneInfo.FindSystemTimeZoneById(runtime.BusinessTimeZone));
        services.AddPortalHttpSecurity(configuration);
        services.AddPortalDataProtection(configuration);
        var authMode = configuration["Authentication:Mode"] ?? AuthenticationModes.Cookie;
        var useBearerTokens = AuthenticationModes.UsesBearerTokens(authMode);
        if (!useBearerTokens && !authMode.Equals(AuthenticationModes.Cookie, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Authentication:Mode must be Cookie, Bearer, or Oidc.");
        }

        var authority = configuration["Authentication:Authority"];
        var audience = configuration["Authentication:Audience"];
        if (useBearerTokens && (string.IsNullOrWhiteSpace(authority) || string.IsNullOrWhiteSpace(audience)))
        {
            throw new InvalidOperationException("Bearer/OIDC authentication requires Authentication:Authority and Authentication:Audience.");
        }

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = useBearerTokens ? Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme : CookieAuthenticationDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = options.DefaultAuthenticateScheme;
            options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        }).AddCookie(
            CookieAuthenticationDefaults.AuthenticationScheme,
            options =>
        {
            options.Cookie.Name = "portal.auth";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            options.ExpireTimeSpan = TimeSpan.FromMinutes(configuration.GetValue("Authentication:Session:AbsoluteMinutes", 30));
            options.SlidingExpiration = false;
            options.Events = new CookieAuthenticationEvents
            {
                OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            },
                OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            },
                OnValidatePrincipal = DisputePortal.Api.Service.Security.PortalCookieSessionValidation.ValidateCookieSession
            };
        }).AddJwtBearer(
            Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme,
            options =>
        {
            options.Authority = authority;
            options.Audience = audience;
            options.RequireHttpsMetadata = !environment.IsDevelopment();
            options.IncludeErrorDetails = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                NameClaimType = configuration["Authentication:NameClaimType"] ?? ClaimTypes.Name,
                RoleClaimType = configuration["Authentication:RoleClaimType"] ?? ClaimTypes.Role,
                ClockSkew = TimeSpan.FromSeconds(runtime.TokenClockSkewSeconds)
            };
            options.Events = new Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerEvents
            {
                OnMessageReceived = context =>
                {

                    if (context.HttpContext.Request.Path == PortalRealtimeHub.EndpointPath
                        && context.HttpContext.WebSockets.IsWebSocketRequest
                        && context.HttpContext.Request.Query.TryGetValue("access_token", out var token)
                        && token.Count == 1
                        && token[0] is { Length: > 0 and <= 8192 })
                    {
                        context.Token = token[0];
                    }

                    return Task.CompletedTask;
                }
            };
        });
        services.AddSignalR(options =>
        {
            options.EnableDetailedErrors = false;
            options.MaximumReceiveMessageSize = 4096;
            options.MaximumParallelInvocationsPerClient = 1;
            options.HandshakeTimeout = TimeSpan.FromSeconds(runtime.RealtimeHandshakeSeconds);
            options.KeepAliveInterval = TimeSpan.FromSeconds(runtime.RealtimeKeepAliveSeconds);
            options.ClientTimeoutInterval = TimeSpan.FromSeconds(runtime.RealtimeClientTimeoutSeconds);
            options.StatefulReconnectBufferSize = 10_000;
        });
        services.AddSingleton<RealtimeConnections>();
        services.AddSingleton<DisputePortal.Api.Interfaces.Services.IRealtimePublisher, RealtimePublisher>();
        services.AddScoped<RealtimeMutationFilter>();
        services.AddHostedService<RealtimeSessionWorker>();
        services.AddSingleton<DisputePortal.Api.Interfaces.Security.ISecretProtector, DisputePortal.Api.Service.Security.SecretProtector>();
        services.AddPortalAuthorization();
        var push = configuration.GetSection("Push").Get<PushOptions>() ?? new();
        push.ValidatePushConfiguration();
        services.AddSingleton(push);
        services.Configure<DisputePortal.Api.Models.Domain.PushProviderOptions>(configuration.GetSection("PushProviders"));
        services.AddSingleton<DisputePortal.Api.Interfaces.Security.IPushEndpointPolicy, DisputePortal.Api.Service.Security.ConfiguredPushEndpointPolicy>();
        var templateDirectory = configuration["MessageTemplates:Directory"];
        var templates = new DisputePortal.Api.Service.ExternalServices.MessageTemplateCatalog(string.IsNullOrWhiteSpace(templateDirectory) ? Path.Combine(
            AppContext.BaseDirectory,
            "Templates",
            "Messages") : templateDirectory);
        templates.ValidateMessageTemplates();
        services.AddSingleton<DisputePortal.Api.Interfaces.ExternalServices.IMessageTemplates>(templates);
        services.AddPortalRedis(configuration);
        services.AddSingleton<DisputePortal.Api.Interfaces.Banking.ICustomerTransactionPresentation>(provider => new DisputePortal.Api.Service.Banking.CustomerTransactionPresentation(
            configuration["TransactionPresentation:File"] ?? Path.Combine(
                AppContext.BaseDirectory,
                "Templates",
                "Transactions",
                "presentation.json"),
            provider.GetService<PortalReferenceCache>()));
        services.AddTransient<PushResponseHandler>();
        services.AddHttpClient<IPushTransport, PushTransport>(client => client.Timeout = TimeSpan.FromSeconds(runtime.PushTimeoutSeconds)).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            SslOptions = new System.Net.Security.SslClientAuthenticationOptions { EnabledSslProtocols = DisputePortal.Platform.Infrastructure.TransportSecurity.Protocols },
            AllowAutoRedirect = false,
            ConnectTimeout = TimeSpan.FromSeconds(runtime.PushConnectTimeoutSeconds),
            MaxConnectionsPerServer = 4,
            PooledConnectionLifetime = TimeSpan.FromSeconds(runtime.PushPoolLifetimeSeconds),
            MaxResponseHeadersLength = 16
        }).AddHttpMessageHandler<PushResponseHandler>().RemoveAllLoggers();
        services.AddScoped<IPushDeliveryService, PushDeliveryService>();
        services.AddScoped<NotificationDeliveryService>();
        services.AddHostedService<DisputePortal.Api.Service.BackgroundJobs.NotificationWorker>();
        services.AddHttpClient<DisputePortal.Api.Interfaces.Services.IWorkflowMessageDelivery, DisputePortal.Api.Service.ExternalServices.InfobipWorkflowDelivery>(client => client.Timeout = TimeSpan.FromSeconds(runtime.MessagingTimeoutSeconds)).ConfigurePrimaryHttpMessageHandler(() => DisputePortal.Platform.Infrastructure.TransportSecurity.HttpHandler());
        var infobip = configuration.GetSection("Infobip").Get<InfobipOptions>() ?? new();
        services.AddSingleton(infobip);
        services.AddSingleton<DisputePortal.Api.Interfaces.Security.IOtpHasher, DisputePortal.Api.Service.Security.OtpHasher>();
        services.AddHttpClient<DisputePortal.Api.Interfaces.Security.IVerificationDelivery, DisputePortal.Api.Service.ExternalServices.InfobipVerificationDelivery>(client => client.Timeout = TimeSpan.FromSeconds(runtime.MessagingTimeoutSeconds)).ConfigurePrimaryHttpMessageHandler(() => DisputePortal.Platform.Infrastructure.TransportSecurity.HttpHandler());
        services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-CSRF-TOKEN";
            options.Cookie.Name = "portal.csrf";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        });
        var origins = configuration.GetSection("AllowedOrigins").Get<string[]>() ?? [];
        services.AddCors(options => options.AddPolicy(
            "portal",
            policy => policy.WithOrigins(origins).AllowAnyMethod().AllowAnyHeader().AllowCredentials().WithExposedHeaders(
                "X-Correlation-ID",
                "X-Total-Count",
                "X-Page",
                "X-Page-Size",
                "X-Has-More")));
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            foreach (var name in ApiRuntimeOptions.FixedWindowPolicies)
            {
                var limit = runtime.RateLimits[name];
                options.AddPolicy(
                    name,
                    context => RateLimitPartition.GetFixedWindowLimiter(
                        name == "auth" ? context.Connection.RemoteIpAddress?.ToString() ?? "unknown" : DisputePortal.Api.Service.Security.RateLimitIdentity.UserOrAddress(context),
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = limit.PermitLimit,
                            Window = TimeSpan.FromSeconds(limit.WindowSeconds),
                            QueueLimit = 0,
                            AutoReplenishment = true
                        }));
            }

            options.AddPolicy(
                "realtime",
                context => RateLimitPartition.GetConcurrencyLimiter(
                         Service.Security.RateLimitIdentity.UserOrAddress(context),
                    _ => new ConcurrencyLimiterOptions
                    {
                        PermitLimit = runtime.RealtimeConnectionLimit,
                        QueueLimit = 0
                    }));
        });
        services.AddScoped<IPasswordHasher<PortalPasswordUser>, PasswordHasher<PortalPasswordUser>>();
        services.AddScoped<DisputePortal.Api.Repository.DevelopmentUserInitializer>();
        services.AddScoped<DisputePortal.Api.Interfaces.Security.IPasswordVerifier, DisputePortal.Api.Service.Security.PortalPasswordVerifier>();
        var healthChecks = services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);
        healthChecks.AddCheck<EvidenceScannerHealthCheck>("evidence-scanner", tags: ["ready"]);
        services.AddPortalMonitoring(configuration);
        services.AddControllers(options => options.Filters.AddService<RealtimeMutationFilter>()).AddJsonOptions(options => options.JsonSerializerOptions.UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow);
        services.AddPortalSwagger();
        var serviceName = configuration["OpenTelemetry:ServiceName"] ?? "transactions-dispute-portal-api";
        var otlpEndpoint = DisputePortal.Platform.Infrastructure.TransportSecurity.MonitoringEndpoint(
            configuration,
            !environment.IsDevelopment());
        var hasOtlpEndpoint = otlpEndpoint is not null;
        services.AddOpenTelemetry().ConfigureResource(resource => resource.AddService(serviceName)).WithTracing(tracing =>
        {
            tracing.AddSource(PortalTelemetry.ActivitySourceName, "DisputePortal.Cache", DisputePortal.Platform.Infrastructure.PlatformMetrics.InstrumentationName)
                .AddAspNetCoreInstrumentation(options => options.RecordException = false)
                .AddHttpClientInstrumentation(options => options.RecordException = false)
                .AddProcessor(new DisputePortal.Platform.Infrastructure.TracePrivacyProcessor());
            if (hasOtlpEndpoint)
            {
                tracing.AddOtlpExporter(options => options.Endpoint = otlpEndpoint!);
            }
        }).WithMetrics(metrics =>
        {
            metrics.AddMeter("Microsoft.AspNetCore.Hosting", PortalTelemetry.MeterName, "DisputePortal.Cache", DisputePortal.Platform.Infrastructure.PlatformMetrics.InstrumentationName);
            if (hasOtlpEndpoint)
            {
                metrics.AddOtlpExporter(options => options.Endpoint = otlpEndpoint!);
            }
        });
        return services;
    }

    public static IDataProtectionBuilder AddPortalDataProtection(this IServiceCollection services, IConfiguration configuration)
    {
        var applicationName = configuration["DataProtection:ApplicationName"];
        var protection = services.AddDataProtection().SetApplicationName(string.IsNullOrWhiteSpace(applicationName) ? "TransactionsDisputePortal" : applicationName);
        var keyDirectory = configuration["DataProtection:KeysDirectory"];
        if (!string.IsNullOrWhiteSpace(keyDirectory))
        {
            Directory.CreateDirectory(keyDirectory);
            protection.PersistKeysToFileSystem(new DirectoryInfo(keyDirectory));
            var certificatePath = configuration["DataProtection:CertificatePath"];
            if (!string.IsNullOrWhiteSpace(certificatePath))
            {
                protection.ProtectKeysWithCertificate(System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12FromFile(
                    certificatePath,
                    configuration["DataProtection:CertificatePassword"]));
            }
            else if (OperatingSystem.IsWindows())
            {
                protection.ProtectKeysWithDpapi();
            }
        }

        return protection;
    }
}
