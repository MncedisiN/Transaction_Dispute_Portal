using DisputePortal.Platform.Contracts;

namespace DisputePortal.Platform.Ledger.Interfaces;

public interface ILedgerCommands
{
    Task<BalanceSnapshot> ReserveAccountFunds(ReserveRequest request, CancellationToken ct);

    Task<JournalResult> Post(PostJournalRequest request, CancellationToken ct);

    Task ReleaseReservedAccountFunds(Guid id, CancellationToken ct);
}
