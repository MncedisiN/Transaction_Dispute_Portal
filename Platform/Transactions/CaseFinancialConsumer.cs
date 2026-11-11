using DisputePortal.Platform.Contracts;
using DisputePortal.Platform.Infrastructure.Interfaces;
using DisputePortal.Platform.Transactions.Interfaces;

namespace DisputePortal.Platform.Transactions;

public class CaseFinancialConsumer(ITransactionStore store) : IDomainEventConsumer
{
    public string[] Subscriptions => ["dispute.financial.requested"];

    public Task Handle(DomainEvent value, CancellationToken ct) => store.AcceptDispute(value, ct);
}
