using DisputePortal.Api.Observability;
using DisputePortal.Api.Models.Domain;

namespace DisputePortal.Api.Interfaces.Observability;

public interface IApiMetricStore
{
    Task Persist(MetricBatch batch, CancellationToken ct);

    Task<MonitoringOverview> Overview(DateTimeOffset from, DateTimeOffset to, int limit, CancellationToken ct);

    Task Prune(DateTimeOffset before, CancellationToken ct);
}
