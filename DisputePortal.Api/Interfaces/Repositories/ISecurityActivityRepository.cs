using DisputePortal.Api.Models.Responses;

namespace DisputePortal.Api.Interfaces.Repositories;

public interface ISecurityActivityRepository
{
    Task<SignInDetailDto?> GetSignInDetail(Guid user, Guid eventId, CancellationToken ct);

    Task<IReadOnlyList<SecurityActivityDto>> GetRecentSignIns(Guid user, CancellationToken ct);

    Task RecordSecurityEvent(Guid? user, string eventType, string outcome, string ip, string userAgent, Guid? session, CancellationToken ct);

    Task<IReadOnlyList<SecurityActivityDto>> GetSecurityActivity(Guid? user, int page, int pageSize, CancellationToken ct);

    Task<IReadOnlyList<ActiveSessionDto>> GetActiveSessions(Guid user, CancellationToken ct);

    Task RevokeOwnedSession(Guid user, Guid session, CancellationToken ct);
}
