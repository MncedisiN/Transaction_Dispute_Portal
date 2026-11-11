using DisputePortal.Platform.Contracts;
using DisputePortal.Platform.Accounts.Interfaces;
using DisputePortal.Platform.Infrastructure.Interfaces;

namespace DisputePortal.Platform.Accounts;

public class AccountConsumer(IAccountCommands commands) : IDomainEventConsumer
{
    public string[] Subscriptions => [EventTypes.UserRegistered, "ledger.account.created", EventTypes.LedgerTransactionPosted, "ledger.reservation.updated", "configuration.updated"];

    public Task Handle(DomainEvent value, CancellationToken ct) => commands.Handle(value, ct);
}
