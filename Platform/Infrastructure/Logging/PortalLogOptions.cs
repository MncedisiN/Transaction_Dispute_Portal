namespace DisputePortal.Platform.Infrastructure.Logging;

public class PortalLogOptions
{
    public int ShutdownTimeoutSeconds { get; set; }
    public string Directory { get; set; } = "";
    public long FileSizeBytes { get; set; }
    public int RetainedFiles { get; set; }
    public int RetentionDays { get; set; }
    public int QueueCapacity { get; set; }
    public bool ConsoleEnabled { get; set; }

    public void ValidateLogRetentionOptions()
    {
        if (ShutdownTimeoutSeconds is < 1 or > 60
            || string.IsNullOrWhiteSpace(Directory)
            || FileSizeBytes is < 1024 or > 100 * 1024 * 1024
            || RetainedFiles is < 1 or > 100
            || RetentionDays is < 1 or > 90
            || QueueCapacity is < 16 or > 8192)
        {
            throw new InvalidOperationException("Application log settings exceed the supported storage bounds.");
        }
    }
}
