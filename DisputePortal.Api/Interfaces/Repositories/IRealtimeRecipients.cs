namespace DisputePortal.Api.Interfaces.Repositories;

public interface IRealtimeRecipients
{
    Task<Guid?> Owner(string entity, Guid id, CancellationToken ct);
}
