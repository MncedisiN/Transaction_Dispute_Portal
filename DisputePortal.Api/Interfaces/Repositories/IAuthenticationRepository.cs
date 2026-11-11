using DisputePortal.Api.Models.Internal;
using DisputePortal.Api.Models.Responses;
using DisputePortal.Api.Models.Domain;

namespace DisputePortal.Api.Interfaces.Repositories;

public interface IAuthenticationRepository
{
    Task<PasswordState?> GetPasswordState(Guid user, CancellationToken ct);

    Task<LoginRecord?> FindLogin(string normalizedIdentifier, CancellationToken cancellationToken);

    Task<Guid> CreateSession(Guid userId, DateTimeOffset expiresAt, CancellationToken cancellationToken);

    Task<bool> IsSessionActive(Guid sessionId, DateTimeOffset now, CancellationToken cancellationToken);

    Task RevokeSession(Guid sessionId, DateTimeOffset now, CancellationToken cancellationToken);

    Task<CurrentUserDto?> GetCurrentUser(Guid userId, CancellationToken cancellationToken);
}
