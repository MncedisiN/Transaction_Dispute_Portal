using DisputePortal.Platform.Contracts;

namespace DisputePortal.Platform.Reporting.Interfaces;

public interface IReportingStore
{
    Task Project(DomainEvent value, CancellationToken ct);

    Task<object> Summary(CancellationToken ct);
}
