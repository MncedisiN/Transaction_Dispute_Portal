using DisputePortal.Api.Models.Domain;

namespace DisputePortal.Api.Interfaces.Repository;

public interface IDisputeInputPolicyStore
{
    Task<DisputeInputPolicy> GetDisputeInputPolicy(CancellationToken ct);
}
