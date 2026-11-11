using DisputePortal.Platform.Contracts;

namespace DisputePortal.Platform.Accounts.Interfaces;

public interface IAccountQueries
{
    Task<AccountRecord[]> GetCustomerAccounts(Guid user, CancellationToken ct);

    Task<AccountRecord> GetAccountById(Guid id, CancellationToken ct);

    Task<object> FindAccountByNumber(string number, CancellationToken ct);
}
