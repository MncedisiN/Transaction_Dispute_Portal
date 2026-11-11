using DisputePortal.Api.Models.Configuration;

namespace DisputePortal.Api.Interfaces.Repositories;

public interface IEvidencePolicyRepository
{
    Task<EvidencePolicy> GetEvidencePolicy(CancellationToken ct);
}
