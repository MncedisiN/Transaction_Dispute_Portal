using DisputePortal.Api.Models.Responses;
using DisputePortal.Api.Models.Requests;

namespace DisputePortal.Api.Interfaces.Repositories;

public interface IProfileRepository
{
    Task<ProfileDto> GetProfile(Guid user, CancellationToken ct);

    Task UpdateProfileDetails(Guid user, ProfileDetailsRequest request, CancellationToken ct);

    Task<string> RevealIdentityNumber(Guid user, CancellationToken ct);

    Task SetIdentity(Guid user, string identityNumber, int birthCentury, CancellationToken ct);
}
