using DisputePortal.Api.Models.Responses;
using DisputePortal.Api.Models.Requests;

namespace DisputePortal.Api.Interfaces.Repositories;

public interface IAccountManagementRepository
{
    Task<AccountNumberDto> RevealAccountNumber(Guid user, Guid account, CancellationToken ct);

    Task<DeletionChallengeDto> ReserveDeletionChallenge(Guid user, string destination, string code, CancellationToken ct);

    Task SetDeletionChallengeReady(Guid user, Guid challenge, bool ready, CancellationToken ct);

    Task<Guid> ConfirmDeletionRequest(Guid user, ConfirmDeletionRequest request, CancellationToken ct);

    Task<IReadOnlyList<AccountDeletionDto>> GetDeletionRequests(Guid actor, bool administration, CancellationToken ct);

    Task CancelDeletionRequest(Guid user, Guid request, CancellationToken ct);

    Task ReviewDeletionRequest(Guid actor, Guid request, ReviewDeletionRequest decision, CancellationToken ct);
}
