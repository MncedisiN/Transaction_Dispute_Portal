using DisputePortal.Api.Configuration;
using Microsoft.Extensions.Configuration;

namespace DisputePortal.TestFixtures;

internal static class TestRuntimeConfiguration
{
    public static ApiRuntimeOptions Read() => ApiRuntimeOptions.LoadRuntimeOptions(new ConfigurationBuilder().AddJsonFile(Path.Combine(
        AppContext.BaseDirectory,
        "appsettings.json")).Build());

    public static DisputePortal.Api.Caching.PortalRedisOptions ApplyRedisConnectionSettings(DisputePortal.Api.Caching.PortalRedisOptions options)
    {
        var configured = new ConfigurationBuilder().AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json")).Build()
            .GetSection("Redis").Get<DisputePortal.Api.Caching.PortalRedisOptions>()!;
        options.ClientName = configured.ClientName;
        options.ReconnectInitialDelayMilliseconds = configured.ReconnectInitialDelayMilliseconds;
        options.ReconnectMaximumDelayMilliseconds = configured.ReconnectMaximumDelayMilliseconds;
        options.DisposalTimeoutMilliseconds = configured.DisposalTimeoutMilliseconds;
        options.FillWaitAttempts = configured.FillWaitAttempts;
        options.FillWaitMilliseconds = configured.FillWaitMilliseconds;
        options.TtlJitterMinimumPercent = configured.TtlJitterMinimumPercent;
        options.TtlJitterMaximumPercent = configured.TtlJitterMaximumPercent;
        return options;
    }

    public static DisputePortal.Platform.Infrastructure.ServiceOptions ApplyServiceMigrationSettings(
        DisputePortal.Platform.Infrastructure.ServiceOptions options)
    {
        using var configuration = new ConfigurationManager();
        DisputePortal.Platform.Infrastructure.IntegrationConfiguration.AddIntegrationDefaults(configuration);
        var configured = configuration.GetRequiredSection("Platform").Get<DisputePortal.Platform.Infrastructure.ServiceOptions>()!;
        options.ServiceTokenLifetimeSeconds = configured.ServiceTokenLifetimeSeconds;
        options.ServiceTokenClockSkewSeconds = configured.ServiceTokenClockSkewSeconds;
        options.ServiceTokenBackdateSeconds = configured.ServiceTokenBackdateSeconds;
        options.MigrationCommandTimeoutSeconds = configured.MigrationCommandTimeoutSeconds;
        options.MigrationLockTimeoutSeconds = configured.MigrationLockTimeoutSeconds;
        return options;
    }
}
