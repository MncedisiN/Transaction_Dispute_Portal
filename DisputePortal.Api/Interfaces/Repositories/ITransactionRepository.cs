using DisputePortal.Api.Models.Internal;
using DisputePortal.Api.Models.Responses;

namespace DisputePortal.Api.Interfaces.Repositories;

public interface ITransactionRepository
{
    Task<IReadOnlyList<TransactionTypeDto>> GetTransactionTypes(CancellationToken ct);

    Task<IReadOnlyList<AccountDto>> GetAccounts(Guid customerId, CancellationToken cancellationToken);

    Task<AccountDto?> GetAccountByPublicId(Guid customerPublicId, Guid accountPublicId, CancellationToken cancellationToken);

    Task<PageResult<TransactionDto>> GetTransactions(Guid customerId, TransactionFilter filter, CancellationToken cancellationToken);

    Task<TransactionDto?> GetTransaction(Guid customerId, Guid transactionId, CancellationToken cancellationToken);
}
