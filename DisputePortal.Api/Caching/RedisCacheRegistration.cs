using Microsoft.Extensions.Caching.StackExchangeRedis;

namespace DisputePortal.Api.Caching;

public static class RedisCacheRegistration
{
    public static IServiceCollection AddPortalRedis(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection("Redis").Get<PortalRedisOptions>() ?? new();
        options.ValidateCacheResourceBounds();
        services.AddSingleton(provider =>
        {
            options.ValidateTransport(provider.GetRequiredService<IHostEnvironment>());
            return options;
        });
        if (!options.Enabled)
        {
            return services;
        }

        services.AddSingleton<PortalRedisConnection>();
        services.AddStackExchangeRedisCache(_ =>
        {
        });
        services.AddOptions<RedisCacheOptions>().Configure<PortalRedisConnection>((cache, connection) => cache.ConnectionMultiplexerFactory = connection.GetRedisConnection);
        services.AddSingleton<PortalReferenceCache>();
        services.AddHealthChecks().AddCheck<RedisCacheHealthCheck>("redis-reference-cache", tags: ["ready"]);
        return services;
    }
}
