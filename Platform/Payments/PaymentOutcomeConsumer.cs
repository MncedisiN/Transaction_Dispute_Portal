using DisputePortal.Platform.Contracts;
using DisputePortal.Platform.Infrastructure.Interfaces;
using DisputePortal.Platform.Payments.Interfaces;

namespace DisputePortal.Platform.Payments;

public class PaymentOutcomeConsumer(IPaymentStore store) : IDomainEventConsumer
{
    public string[] Subscriptions => [EventTypes.TransactionCompleted, EventTypes.TransactionFailed];

    public Task Handle(DomainEvent value, CancellationToken ct) => store.CompletePaymentIntent(value, ct);
}
