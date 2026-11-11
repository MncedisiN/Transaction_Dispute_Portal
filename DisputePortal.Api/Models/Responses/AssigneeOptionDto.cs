namespace DisputePortal.Api.Models.Responses;

public class AssigneeOptionDto
{
    public Guid Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public int ActiveCases { get; set; }
    public int Capacity { get; set; }
    public bool Eligible { get; set; }
    public string UnavailableReason { get; set; } = string.Empty;
}
