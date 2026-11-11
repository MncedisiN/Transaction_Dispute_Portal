using DisputePortal.Platform.Contracts;
using DisputePortal.Platform.Transactions.Models;

namespace DisputePortal.Platform.Transactions.Interfaces;

public interface ITransactionStore
{
    Task Accept(DomainEvent value, CancellationToken ct);

    Task AcceptDispute(DomainEvent value, CancellationToken ct);

    Task<TransactionRecord> Reverse(ReversalPreparation request, TransactionRecord original, CancellationToken ct);

    Task<WorkItem?> Claim(CancellationToken ct);

    Task Transition(WorkItem work, string status, string? code, CancellationToken ct);

    Task RetryTransactionProcessing(WorkItem work, CancellationToken ct);

    Task<object> GetCustomerTransactions(Guid user, CancellationToken ct);

    Task<TransactionRecord> GetTransactionById(Guid id, Guid? user, CancellationToken ct);
}
