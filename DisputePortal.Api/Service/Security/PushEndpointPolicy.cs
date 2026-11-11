using DisputePortal.Api.Models.Domain;

namespace DisputePortal.Api.Service.Security;
// Compatibility facade for existing callers; the allowlist is data, not code.
public static class PushEndpointPolicy
{
    public static bool IsAllowed(string endpoint)
    {
        var config = new ConfigurationBuilder().AddJsonFile(Path.Combine(AppContext.BaseDirectory, "Configuration", "push-providers.json")).Build();
        try
        {
            return ConfiguredPushEndpointPolicy.Allows(endpoint, config.GetSection("PushProviders").Get<PushProviderOptions>() ?? new());
        }
        finally
        {
            (config as IDisposable)?.Dispose();
        }
    }
}
