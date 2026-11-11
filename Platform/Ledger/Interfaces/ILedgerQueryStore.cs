using DisputePortal.Platform.Contracts;
using DisputePortal.Platform.Ledger.Domain;

namespace DisputePortal.Platform.Ledger.Interfaces;

public interface ILedgerQueryStore
{
    Task<LedgerPosition> ReadPosition(Guid id, CancellationToken ct);

    Task<JournalResult?> TryPosted(PostJournalRequest request, CancellationToken ct);

    Task<JournalResult> GetJournal(Guid id, CancellationToken ct);

    Task<object> Entries(Guid id, Guid user, CancellationToken ct);

    Task<object> Reconcile(CancellationToken ct);
}
