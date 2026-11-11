using DisputePortal.Api.Models.Responses;

namespace DisputePortal.Api.Repository.Interfaces;

public interface IBankingProvisioningRepository
{
    Task<bool> EnsureFirstLoginAccounts(Guid customer, CancellationToken ct);

    Task<BankingProvisioningStatusDto> GetProvisioningStatus(Guid customer, CancellationToken ct);

    Task<ProvisionedAccountCreatedDto> OpenAccount(Guid customer, Guid requestId, string accountType, int count, CancellationToken ct);

    Task<BankingBatchDto> QueueTransactions(Guid customer, Guid account, Guid requestId, int count, CancellationToken ct);

    Task<bool> ProcessNextTransactionBatch(CancellationToken ct);
}
