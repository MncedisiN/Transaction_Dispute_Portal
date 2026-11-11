namespace DisputePortal.Platform.Contracts;

public class SettingsUpdate
{
    public long ConcurrencyToken { get; set; }
    public Guid RequestId { get; set; }
    public Guid? SourceVersionPublicId { get; set; }
    public string Operation { get; set; } = "Create";
    public int ExpectedVersion { get; set; }
    public PlatformSettings Settings { get; set; } = null!;
    public string Reason { get; set; } = "";

    public SettingsUpdate()
    {
    }

    public SettingsUpdate(int ExpectedVersion, PlatformSettings Settings, string Reason)
    {
        this.ExpectedVersion = ExpectedVersion;
        ConcurrencyToken = ExpectedVersion;
        RequestId = Guid.NewGuid();
        this.Settings = Settings;
        this.Reason = Reason;
    }
}
