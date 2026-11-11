namespace DisputePortal.Api.Models.Domain;

public class MonitoringOverview
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public long RequestCount { get; set; }
    public long FailureCount { get; set; }
    public long ClientErrorCount { get; set; }
    public double AverageDurationMs { get; set; }
    public DateTimeOffset? LastPersistedAt { get; set; }
    public IReadOnlyList<StoredMetric> Endpoints { get; set; } = [];

    public MonitoringOverview()
    {
    }

    public MonitoringOverview(
        DateTimeOffset From,
        DateTimeOffset To,
        long RequestCount,
        long FailureCount,
        long ClientErrorCount,
        double AverageDurationMs,
        DateTimeOffset? LastPersistedAt,
        IReadOnlyList<StoredMetric> Endpoints)
    {
        this.From = From;
        this.To = To;
        this.RequestCount = RequestCount;
        this.FailureCount = FailureCount;
        this.ClientErrorCount = ClientErrorCount;
        this.AverageDurationMs = AverageDurationMs;
        this.LastPersistedAt = LastPersistedAt;
        this.Endpoints = Endpoints;
    }
}
