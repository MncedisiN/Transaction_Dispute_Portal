using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;

namespace DisputePortal.Platform.Infrastructure;

public static class ServiceHttpConfiguration
{
    public static IHttpClientBuilder AddServiceHttpClient(this IServiceCollection services, IConfiguration configuration)
    {
        var client = services.AddHttpClient<Interfaces.IServiceClient, ServiceClient>(http => http.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(() =>
            {
                var transport = configuration.GetRequiredSection("ServiceHttpTransport");
                var handler = new SocketsHttpHandler
                {
                    AllowAutoRedirect = false,
                    UseCookies = false,
                    PooledConnectionLifetime = transport.GetRequiredSection("PooledConnectionLifetime").Get<TimeSpan>(),
                    PooledConnectionIdleTimeout = transport.GetRequiredSection("PooledConnectionIdleTimeout").Get<TimeSpan>(),
                    ConnectTimeout = transport.GetRequiredSection("ConnectTimeout").Get<TimeSpan>(),
                    MaxConnectionsPerServer = transport.GetRequiredSection("MaxConnectionsPerServer").Get<int>()
                };
                handler.SslOptions.EnabledSslProtocols = TransportSecurity.Protocols;
                return handler;
            });
        client.AddStandardResilienceHandler(options =>
        {
            configuration.GetRequiredSection("ServiceHttpResilience").Bind(options); 
            options.Retry.DisableForUnsafeHttpMethods();
        }).SelectPipelineByAuthority();
        return client;
    }
}
