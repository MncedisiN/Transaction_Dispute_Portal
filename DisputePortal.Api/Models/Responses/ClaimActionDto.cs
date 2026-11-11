namespace DisputePortal.Api.Models.Responses;

public class ClaimActionDto
{
    public bool Allowed { get; set; }
    public string State { get; set; } = "locked";
    public string Label { get; set; } = "Case Locked";
    public string Reason { get; set; } = string.Empty;
}
