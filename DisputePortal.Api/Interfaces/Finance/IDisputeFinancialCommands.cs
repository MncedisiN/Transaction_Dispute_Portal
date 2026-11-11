using System.Data.Common;
using DisputePortal.Api.Models.Responses;
using DisputePortal.Api.Models.Domain;

namespace DisputePortal.Api.Interfaces.Finance;

public interface IDisputeFinancialCommands
{
    Task<DisputeFinancialCommand> CreateFinancialCommand(
        DbConnection c,
        DbTransaction tx,
        Guid dispute,
        Guid original,
        Guid account,
        Guid user,
        string kind,
        decimal amount,
        string currency,
        Guid? actor,
        Guid request,
        string reason,
        int? policy,
        int expectedVersion,
        Guid countryClaimRequest,
        CancellationToken ct);

    Task ReleaseUncommitted(DbConnection c, Guid request, Guid countryClaimRequest, CancellationToken ct);

    Task<PendingFinancialDto?> Pending(DbConnection c, Guid dispute, CancellationToken ct);

    Task<IReadOnlyList<AccountBalanceDto>> Balances(Guid user, CancellationToken ct);

    Task<bool> CanSettleHistorical(Guid account, Guid user, string currency, CancellationToken ct);
}
