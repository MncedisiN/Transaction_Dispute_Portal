using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace DisputePortal.Api.Observability;
// HTTP measurements belong to Microsoft.AspNetCore.Hosting. This meter contains
// only application-specific diagnostics about the collection/storage pipeline.
public sealed class PortalTelemetry : IDisposable
{
    public const string MeterName = "DisputePortal.Api";
    public const string ActivitySourceName = "DisputePortal.Api.Operations";
    private readonly Meter meter = new(MeterName, "2.0.0");
    public ActivitySource Activities { get; } = new(ActivitySourceName, "2.0.0");

    private readonly Counter<long> failures;
    private readonly Counter<long> dropped;
    private readonly Counter<long> batches;

    public PortalTelemetry()
    {
        failures = meter.CreateCounter<long>("portal.telemetry.storage.failures", "{failure}");
        dropped = meter.CreateCounter<long>("portal.telemetry.measurements.dropped", "{measurement}");
        batches = meter.CreateCounter<long>("portal.telemetry.batches.persisted", "{batch}");
    }

    public void StorageFailed() => failures.Add(1);

    public void MeasurementDropped() => dropped.Add(1);

    public void BatchPersisted() => batches.Add(1);

    public void Dispose()
    {
        Activities.Dispose();
        meter.Dispose();
    }
}
