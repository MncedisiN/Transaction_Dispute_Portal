using DisputePortal.Api.Models.Requests;
using DisputePortal.Api.Models.Responses;

namespace DisputePortal.Api.Interfaces.Repositories;

public interface IFinanceRepository
{
    Task<BalanceOverviewDto> GetOverview(Guid customer, CancellationToken ct);

    Task<IReadOnlyList<AccountBalanceDto>> GetBalances(Guid customer, CancellationToken ct);

    Task<IReadOnlyList<BalanceHistoryDto>> GetBalanceHistory(Guid customer, Guid account, CancellationToken ct);

    Task<FinancialSummaryDto> GetCaseFinancials(Guid actor, Guid dispute, CancellationToken ct);

    Task<IReadOnlyList<CaseActivityDto>> GetCaseActivity(Guid actor, Guid dispute, CancellationToken ct);

    Task<RefundDto> ApproveRefund(Guid actor, Guid dispute, ApproveRefundRequest request, string correlation, CancellationToken ct);

    Task<RefundRecallDto> RecallRefund(Guid actor, Guid dispute, RecallRefundRequest request, string correlation, CancellationToken ct);

    Task<AutomaticRefundPolicyDto> GetPolicy(Guid actor, CancellationToken ct);

    Task UpdatePolicy(Guid actor, UpdateAutomaticRefundPolicyRequest request, CancellationToken ct);

    Task<bool> ProcessAutomatic(CancellationToken ct);
}
