using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;

namespace DisputePortal.Platform.Infrastructure;

public static class IntegrationConfiguration
{
    public static ServiceOptions ReadServiceOptions(string privateSettingsPath)
    {
        using var configuration = new ConfigurationManager();
        configuration.AddIntegrationDefaults();
        configuration.AddJsonFile(Path.GetFullPath(privateSettingsPath), false, false).AddEnvironmentVariables();
        return configuration.GetRequiredSection("Platform").Get<ServiceOptions>() ?? throw new InvalidOperationException("Service settings are required.");
    }

    public static void AddIntegrationDefaults(this ConfigurationManager configuration)
    {
        // Lowest precedence: appsettings, private files and environment overrides win.
        var source = new JsonConfigurationSource
        {
            Path = Path.Combine(AppContext.BaseDirectory, "Configuration", "integrations.json"),
            Optional = false,
            ReloadOnChange = false
        };
        source.ResolveFileProvider();
        configuration.Sources.Insert(0, source);
    }
}
