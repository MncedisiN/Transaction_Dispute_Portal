namespace DisputePortal.Api.Observability;

public class MetricSummary
{
    public MetricKey Key { get; set; } = null!;
    public long RequestCount { get; set; }
    public long FailureCount { get; set; }
    public long ClientErrorCount { get; set; }
    public double TotalDurationMs { get; set; }
    public double MinimumDurationMs { get; set; }
    public double MaximumDurationMs { get; set; }

    public MetricSummary()
    {
    }

    public MetricSummary(MetricSummary source)
    {
        Key = source.Key;
        RequestCount = source.RequestCount;
        FailureCount = source.FailureCount;
        ClientErrorCount = source.ClientErrorCount;
        TotalDurationMs = source.TotalDurationMs;
        MinimumDurationMs = source.MinimumDurationMs;
        MaximumDurationMs = source.MaximumDurationMs;
    }
}
