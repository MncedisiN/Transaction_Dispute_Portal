using DisputePortal.Platform.Contracts;
using DisputePortal.Platform.Infrastructure.Interfaces;

namespace DisputePortal.Platform.Configuration;

public class CountryOperationConsumer(CountryOperationStore store) : IDomainEventConsumer
{
    public string[] Subscriptions => [EventTypes.AccountActivated, EventTypes.PaymentCompleted, EventTypes.PaymentFailed, EventTypes.TransactionCompleted, EventTypes.TransactionFailed, "country.operation.settled", "ledger.import.settled", "debt.reviewed"];

    public Task Handle(DomainEvent value, CancellationToken ct) => store.CompleteCountryOperation(value, ct);
}
