using DisputePortal.Api.Models.Internal;
using DisputePortal.Api.Models.Responses;

namespace DisputePortal.Api.Interfaces.Repositories;

public interface IIdentityVerificationRepository
{
    Task<VerifiedIdentity?> FindIdentity(string contact, CancellationToken ct);

    Task<bool> RegistrationContactExists(string contact, CancellationToken ct);

    Task<IdentityChallengeDto> CreateIdentityChallenge(
        Guid? user,
        string purpose,
        string channel,
        string destination,
        string payload,
        string code,
        CancellationToken ct);

    Task MarkIdentityChallengeReady(Guid challenge, CancellationToken ct);

    Task<Guid> CompleteRegistration(Guid challenge, string code, CancellationToken ct);

    Task<Guid> RegisterWithoutContactVerification(RegistrationPayload registration, CancellationToken ct);

    Task<Guid> CompleteLogin(Guid challenge, string code, CancellationToken ct);

    Task CompletePasswordReset(
        Guid challenge,
        string code,
        string passwordHash,
        CancellationToken ct,
        Func<string, bool>? matchesCurrentPassword = null);
}
