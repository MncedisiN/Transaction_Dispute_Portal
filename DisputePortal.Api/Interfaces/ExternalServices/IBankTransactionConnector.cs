using DisputePortal.Api.Models.Banking;

namespace DisputePortal.Api.Interfaces.ExternalServices; 
public interface IBankTransactionConnector
{
    string Provider { get; }

    Task<BankTransactionPage> ReadTransactions(string externalAccountId, string? cursor, int pageSize, CancellationToken cancellationToken);
}
