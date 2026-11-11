namespace DisputePortal.Platform.Contracts;

public class SettingsRevision : PolicyVersionMetadata
{
    public PlatformSettings Settings { get; set; } = null!;

    [System.Text.Json.Serialization.JsonIgnore]
    public string? Body { get; set; }
}
