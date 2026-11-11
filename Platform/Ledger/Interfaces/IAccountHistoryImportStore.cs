using DisputePortal.Platform.Contracts;

namespace DisputePortal.Platform.Ledger.Interfaces;

public interface IAccountHistoryImportStore
{
    Task<AccountHistoryImportResult> Import(AccountHistorySnapshot book, CancellationToken ct);
}
