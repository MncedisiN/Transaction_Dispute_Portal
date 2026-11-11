using DisputePortal.Api.Models.Requests;
using DisputePortal.Api.Models.Responses;

namespace DisputePortal.Api.Interfaces.Accounts;

public interface IAccountClosureService
{
    Task<DeletionChallengeDto> StartAccountClosure(Guid userId, StartDeletionRequest request, CancellationToken cancellationToken);
}
