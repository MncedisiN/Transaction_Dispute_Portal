using DisputePortal.Platform.Contracts;
using DisputePortal.Platform.Infrastructure.Interfaces;

namespace DisputePortal.Platform.Accounts;

public class AccountCountryPolicyJob(AccountStore store, IServiceClient client) : IPersistentJobRunner
{
    private int appliedVersion = -1;

    public async Task RunScheduledWork(CancellationToken ct)
    {
        var settings = await client.SendServiceRequest<PlatformSettings>("configuration", HttpMethod.Get, "/settings", ct: ct);
        if (settings.Version == appliedVersion)
        {
            return;
        }

        await store.ApplyCountryPolicy(settings, ct);
        appliedVersion = settings.Version;
    }
}
