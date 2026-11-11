using DisputePortal.Platform.Infrastructure;
using Microsoft.Extensions.Configuration;

namespace DisputePortal.Platform.Tests;

internal static class TestMigrationConfiguration
{
    public static ServiceOptions ApplyServiceMigrationSettings(ServiceOptions options)
    {
        using var configuration = new ConfigurationManager();
        configuration.AddIntegrationDefaults();
        var configured = configuration.GetRequiredSection("Platform").Get<ServiceOptions>()!;
        options.ServiceTokenLifetimeSeconds = configured.ServiceTokenLifetimeSeconds;
        options.ServiceTokenClockSkewSeconds = configured.ServiceTokenClockSkewSeconds;
        options.ServiceTokenBackdateSeconds = configured.ServiceTokenBackdateSeconds;
        options.CleanupTimeoutSeconds = configured.CleanupTimeoutSeconds;
        options.MigrationCommandTimeoutSeconds = configured.MigrationCommandTimeoutSeconds;
        options.MigrationLockTimeoutSeconds = configured.MigrationLockTimeoutSeconds;
        return options;
    }
}
