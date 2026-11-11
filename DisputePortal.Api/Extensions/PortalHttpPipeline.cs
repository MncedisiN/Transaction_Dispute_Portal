using System.Diagnostics;
using DisputePortal.Api.Configuration;
using DisputePortal.Api.Middleware;
using DisputePortal.Api.Observability;
using DisputePortal.Api.Realtime;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Serilog;

namespace DisputePortal.Api.Extensions; 
public static class PortalHttpPipeline
{
    public static IServiceCollection AddPortalHttpSecurity(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddProblemDetails(options => options.CustomizeProblemDetails = PortalProblems.Customize);
        services.AddExceptionHandler<PortalExceptionHandler>();
        services.AddHttpContextAccessor();
        services.Configure<Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerOptions>(DisputePortal.Platform.Infrastructure.TransportSecurity.ConfigureHttps);
        services.AddHttpsRedirection(options => options.HttpsPort = configuration.GetValue("HttpsRedirection:HttpsPort", 443));
        services.AddHsts(options => configuration.GetSection("Hsts").Bind(options));
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            var proxies = configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [];
            var networks = configuration.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>() ?? []; 
            options.ForwardedHeaders = proxies.Length + networks.Length == 0 ? ForwardedHeaders.None : ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = configuration.GetValue("ForwardedHeaders:ForwardLimit", 1);
            if (options.ForwardLimit is < 1 or > 4)
            {
                throw new InvalidOperationException("ForwardLimit must be between one and four.");
            }

            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();
            foreach (var proxy in proxies)
                options.KnownProxies.Add(System.Net.IPAddress.Parse(proxy));
            foreach (var network in networks)
            {
                var parsed = System.Net.IPNetwork.Parse(network);
                if (parsed.PrefixLength == 0)
                {
                    throw new InvalidOperationException("A universal forwarded-header network is not permitted.");
                }

                options.KnownIPNetworks.Add(parsed);
            }
        });
        return services;
    }

    public static WebApplication UsePortalHttpPipeline(this WebApplication app)
    {
        app.UseMiddleware<BrowserSecurityHeadersMiddleware>();
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseSerilogRequestLogging(options => options.EnrichDiagnosticContext = (diagnostics, context) =>
        {
            diagnostics.Set(
                "Route",
                context.RequestServices.GetRequiredService<EndpointMetricCatalog>().GetNormalizedEndpointRoute(context));
            diagnostics.Set("Method", EndpointMetricCatalog.Method(context.Request.Method));
            diagnostics.Set("TraceId", Activity.Current?.TraceId.ToString() ?? "");
            diagnostics.Set("CorrelationId", context.Items["CorrelationId"]?.ToString() ?? "");
        });
        app.UseExceptionHandler();
        app.UseForwardedHeaders();
        app.UseStatusCodePages(async status =>
        {
            var context = status.HttpContext;
            await PortalProblems.WriteSanitizedProblemDetails(
                context,
                context.RequestServices.GetRequiredService<IProblemDetailsService>(),
                new ProblemDetails { Status = context.Response.StatusCode },
                context.RequestAborted);
        });
        if (!app.Environment.IsDevelopment())
        {
            app.UseHsts();
            app.UseHttpsRedirection();
        }

        app.UseRouting();
        app.UseCors("portal");
        app.UseWebSockets();
        app.UseMiddleware<RealtimeOriginMiddleware>();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();
        app.UseAntiforgery();
        app.UseMiddleware<CsrfValidationMiddleware>();
        return app;
    }

    public static T RequirePortalAntiforgery<T>(this T endpoints, IConfiguration configuration)
        where T : IEndpointConventionBuilder
    {
        if (!AuthenticationModes.UsesBearerTokens(configuration["Authentication:Mode"]))
        {
            endpoints.WithMetadata(new RequireAntiforgeryTokenAttribute());
        }

        return endpoints;
    }
}
