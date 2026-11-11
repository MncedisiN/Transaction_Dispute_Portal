using DisputePortal.Platform.Contracts;

namespace DisputePortal.Platform.Transactions.Interfaces;

public interface IHistoryStore
{
    Task<object> Import(AccountHistorySnapshot book, CancellationToken ct);

    Task<TransactionRecord[]> GetCustomerTransactionHistory(Guid user, CancellationToken ct);

    Task<TransactionRecord?> GetTransactionHistoryEntry(Guid id, Guid? user, CancellationToken ct);
}
