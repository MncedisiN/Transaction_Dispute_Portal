using DisputePortal.Api.Models.Responses;

namespace DisputePortal.Api.Interfaces.Repositories;

public interface IContactVerificationRepository
{
    Task<ContactChallengeDto> ReserveContactChallenge(
        Guid user,
        string field,
        string destination,
        string channel,
        string code,
        CancellationToken ct);

    Task SetContactChallengeDelivery(Guid user, Guid challenge, bool delivered, CancellationToken ct);

    Task CompleteContactChange(Guid user, Guid challenge, string code, CancellationToken ct);
}
