namespace DisputePortal.Api.Models.Domain;

public class StoredMetric
{
    public string Method { get; set; } = "";
    public string Route { get; set; } = "";
    public int StatusCode { get; set; }
    public long RequestCount { get; set; }
    public long FailureCount { get; set; }
    public long ClientErrorCount { get; set; }
    public double TotalDurationMs { get; set; }
    public double MinimumDurationMs { get; set; }
    public double MaximumDurationMs { get; set; }
    public double AverageDurationMs => RequestCount == 0 ? 0 : TotalDurationMs / RequestCount;
}
