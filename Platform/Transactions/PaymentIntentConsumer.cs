using DisputePortal.Platform.Contracts;
using DisputePortal.Platform.Infrastructure.Interfaces;
using DisputePortal.Platform.Transactions.Interfaces;

namespace DisputePortal.Platform.Transactions;

public class PaymentIntentConsumer(ITransactionStore store) : IDomainEventConsumer
{
    public string[] Subscriptions => [EventTypes.PaymentInitiated, "payment.authorized"];

    public Task Handle(DomainEvent value, CancellationToken ct) => store.Accept(value, ct);
}
