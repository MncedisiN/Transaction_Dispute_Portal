using DisputePortal.Api.Models.Domain;
using DisputePortal.Api.Models.Requests;
using DisputePortal.Api.Models.Responses;

namespace DisputePortal.Api.Interfaces.Repositories;

public interface IDisputeRiskRepository
{
    Task<PolicyDefaultsDto> GetPolicyDefaults(CancellationToken ct);

    Task<DisputeRiskPolicy> GetRiskPolicy(CancellationToken ct);

    Task<PolicyConfigurationDto> ResetPolicies(Guid actor, ResetDisputePoliciesRequest request, CancellationToken ct);

    Task UpdateRiskPolicy(Guid actor, RiskPolicyUpdateRequest request, CancellationToken ct);

    Task<DisputeIntakeDto> AssessDispute(Guid user, Guid transaction, CancellationToken ct);

    Task<IdentityChallengeDto> ReserveDisputeVerification(
        Guid user,
        Guid session,
        Guid transaction,
        string channel,
        string destination,
        string code,
        CancellationToken ct);

    Task ReadyDisputeVerification(Guid user, Guid challenge, CancellationToken ct);

    Task CompleteDisputeVerification(Guid user, Guid session, Guid transaction, Guid challenge, string code, CancellationToken ct);
}
