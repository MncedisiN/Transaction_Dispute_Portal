using DisputePortal.Platform.Contracts;

namespace DisputePortal.Platform.Accounts.Interfaces;

public interface IAccountCommands
{
    Task<AccountRecord> OpenCustomerAccount(Guid user, OpenAccountRequest request, CancellationToken ct);

    Task Handle(DomainEvent value, CancellationToken ct);
}
