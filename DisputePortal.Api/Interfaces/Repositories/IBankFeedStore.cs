using DisputePortal.Api.Models.Banking;
using DisputePortal.Api.Models.Domain;

namespace DisputePortal.Api.Interfaces.Repositories;

public interface IBankFeedStore
{ 
    Task LinkOwnedAccount(Guid customerId, Guid accountId, string provider, string externalAccountId, CancellationToken ct);

    Task<BankFeedBinding?> GetOwnedBinding(Guid customerId, Guid accountId, CancellationToken ct);

    Task<int> ApplyPage(Guid customerId, BankFeedBinding expected, BankTransactionPage page, CancellationToken ct);
}
