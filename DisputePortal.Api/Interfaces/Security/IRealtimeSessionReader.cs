namespace DisputePortal.Api.Interfaces.Security;

public interface IRealtimeSessionReader
{
    Task<bool> IsActive(Guid user, Guid session, DateTimeOffset now, CancellationToken ct);
}
