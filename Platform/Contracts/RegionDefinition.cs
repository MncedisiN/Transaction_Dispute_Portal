namespace DisputePortal.Platform.Contracts;

public class RegionDefinition
{
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string[] Districts { get; set; } = null!;
}
