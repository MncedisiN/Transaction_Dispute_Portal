using DisputePortal.Platform.Infrastructure.Interfaces;
using DisputePortal.Platform.Ledger.Interfaces;

namespace DisputePortal.Platform.Ledger;

public class LedgerCountryPolicyJob(ILedgerAccountWriter store, IFinancialConfiguration configuration) : IPersistentJobRunner
{
    private int appliedVersion = -1;

    public async Task RunScheduledWork(CancellationToken ct)
    {
        var settings = await configuration.GetFinancialConfiguration(ct);
        if (settings.Version == appliedVersion)
        {
            return;
        }

        await store.ApplyCountryPolicy(settings, ct);
        appliedVersion = settings.Version;
    }
}
