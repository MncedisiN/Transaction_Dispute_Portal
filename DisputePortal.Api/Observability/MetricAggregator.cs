namespace DisputePortal.Api.Observability;

public class MetricAggregator(TelemetryOptions options, TimeProvider clock, PortalTelemetry telemetry)
{
    private readonly object gate = new();
    private readonly Dictionary<MetricKey, MetricSummary> summaries = [];
    private long dropped;
    public int BufferedSeries
    {
        get
        {
            lock (gate)
                return summaries.Count;
        }
    }

    public long DroppedMeasurements => Interlocked.Read(ref dropped);

    public void Observe(string method, string route, int statusCode, double seconds, bool error)
    {
        if (!double.IsFinite(seconds) || seconds < 0)
        {
            return;
        }

        var now = clock.GetUtcNow();
        var minute = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, TimeSpan.Zero);
        var key = new MetricKey
        {
            BucketStart = minute,
            Method = EndpointMetricCatalog.Method(method),
            Route = route,
            StatusCode = statusCode is >= 100 and <= 599 ? statusCode : 0
        };
        var ms = Math.Min(seconds, 86400d * 365) * 1000d;
        lock (gate)
        {
            if (!summaries.TryGetValue(key, out var existing))
            {
                if (summaries.Count >= options.MaximumSeries)
                {
                    Interlocked.Increment(ref dropped);
                    telemetry.MeasurementDropped();
                    return;
                }

                existing = new global::DisputePortal.Api.Observability.MetricSummary
                {
                    Key = key,
                    RequestCount = 0,
                    FailureCount = 0,
                    ClientErrorCount = 0,
                    TotalDurationMs = 0,
                    MinimumDurationMs = ms,
                    MaximumDurationMs = ms
                };
            }

            summaries[key] = new global::DisputePortal.Api.Observability.MetricSummary(existing)
            {
                RequestCount = existing.RequestCount + 1,
                FailureCount = existing.FailureCount + (error || statusCode >= 500 ? 1 : 0),
                ClientErrorCount = existing.ClientErrorCount + (statusCode is >= 400 and < 500 ? 1 : 0),
                TotalDurationMs = existing.TotalDurationMs + ms,
                MinimumDurationMs = Math.Min(existing.MinimumDurationMs, ms),
                MaximumDurationMs = Math.Max(existing.MaximumDurationMs, ms)
            };
        }
    }

    public MetricBatch? TakeBatch()
    {
        lock (gate)
        {
            if (summaries.Count == 0)
            {
                return null;
            }

            var rows = summaries.Values.OrderBy(row => row.Key.BucketStart).Take(options.BatchSize).ToArray();
            foreach (var row in rows)
                summaries.Remove(row.Key);
            return new global::DisputePortal.Api.Observability.MetricBatch
            {
                Id = Guid.NewGuid(),
                Summaries = rows
            };
        }
    }
}
