namespace DisputePortal.Api.Observability;

public class TelemetryOptions
{
    public int DefaultLookbackHours { get; set; }
    public int FlushSeconds { get; set; }
    public int MaximumSeries { get; set; }
    public int BatchSize { get; set; }
    public int RetentionDays { get; set; }
    public int StorageTimeoutSeconds { get; set; }

    public void ValidateTelemetryConfiguration()
    {
        if (DefaultLookbackHours is < 1 or > 168
            || FlushSeconds is < 1 or > 300
            || MaximumSeries is < 16 or > 16384
            || BatchSize is < 1 or > 512
            || RetentionDays is < 1 or > 90
            || StorageTimeoutSeconds is < 1 or > 30)
        {
            throw new InvalidOperationException("Monitoring settings exceed the supported resource bounds.");
        }
    }
}
