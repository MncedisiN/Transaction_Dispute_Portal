using DisputePortal.Api.Models.Responses;

namespace DisputePortal.Api.Interfaces.Repositories;

public interface ISupportReadRepository
{
    Task<SupportThreadDto?> GetSupportThread(Guid actor, Guid thread, CancellationToken ct);

    Task MarkSupportRead(Guid actor, Guid thread, Guid message, CancellationToken ct);

    Task<Guid?> GetLastSupportThread(Guid actor, CancellationToken ct);
}
