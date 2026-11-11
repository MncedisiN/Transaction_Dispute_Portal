using DisputePortal.Platform.Contracts;
using DisputePortal.Platform.Ledger.Domain;

namespace DisputePortal.Platform.Ledger.Interfaces;

public interface ILedgerCommandStore
{
    Task<BalanceSnapshot> ReserveAccountFunds(ReserveRequest request, PlatformSettings settings, CancellationToken ct);

    Task<JournalResult> Post(PostingPlan plan, PlatformSettings settings, CancellationToken ct);

    Task ReleaseReservedAccountFunds(Guid id, CancellationToken ct);
}
