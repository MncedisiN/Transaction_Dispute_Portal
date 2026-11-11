namespace DisputePortal.Platform.Contracts;

public class SettingsRemovalRequest
{
    public long ConcurrencyToken { get; set; }
    public string Reason { get; set; } = "";
}
