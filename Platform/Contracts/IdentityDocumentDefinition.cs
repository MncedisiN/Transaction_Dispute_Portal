namespace DisputePortal.Platform.Contracts;

public class IdentityDocumentDefinition
{
    public string Key { get; set; } = null!;
    public string Label { get; set; } = null!;
    public string Pattern { get; set; } = null!;
    public string Validator { get; set; } = null!;
    public bool Required { get; set; } = true;
}
