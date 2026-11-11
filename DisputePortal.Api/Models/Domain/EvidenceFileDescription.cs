namespace DisputePortal.Api.Models.Domain;

public class EvidenceFileDescription
{
    public string FileName { get; set; } = null!;
    public string ContentType { get; set; } = null!;
    public string Sha256 { get; set; } = null!;
}
