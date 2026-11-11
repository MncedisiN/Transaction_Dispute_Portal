using DisputePortal.Api.Models.Responses;

namespace DisputePortal.Api.Interfaces.Repositories;

public interface IAuditRepository
{
    Task<PageResult<AuditRecordDto>> GetAuditLogs(int page, int pageSize, string? action, CancellationToken cancellationToken);
}
