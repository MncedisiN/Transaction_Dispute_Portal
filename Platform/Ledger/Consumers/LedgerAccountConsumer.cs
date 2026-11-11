using DisputePortal.Platform.Contracts;
using DisputePortal.Platform.Infrastructure.Interfaces;
using DisputePortal.Platform.Ledger.Interfaces;

namespace DisputePortal.Platform.Ledger;

public class LedgerAccountConsumer(ILedgerAccountWriter accounts) : IDomainEventConsumer
{
    public string[] Subscriptions => [EventTypes.AccountCreated];

    public Task Handle(DomainEvent value, CancellationToken ct) => accounts.HandleAccountCreated(value, ct);
}
