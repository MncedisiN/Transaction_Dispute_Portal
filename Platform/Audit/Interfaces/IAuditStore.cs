using DisputePortal.Platform.Contracts;

namespace DisputePortal.Platform.Audit.Interfaces;

public interface IAuditStore
{
    Task Append(DomainEvent value, CancellationToken ct);

    Task<object> GetAuditEntries(Guid? correlation, CancellationToken ct);
}
