namespace DisputePortal.Platform.Contracts;

public class SettingsHistory
{
    public IReadOnlyList<SettingsRevision> Items { get; set; } = [];
    public long Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public long ConcurrencyToken { get; set; }
    public PolicyResolution<SettingsRevision> Effective { get; set; } = new();
}
