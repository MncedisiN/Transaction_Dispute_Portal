namespace DisputePortal.Api.Models.Responses;

public class StaffUserDto
{
    public string? FirstName { get; set; }
    public string? MiddleName { get; set; }
    public string? LastName { get; set; }
    public Guid Id { get; set; }
    public string DisplayName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Role { get; set; } = "";
    public string Status { get; set; } = "";
    public string Availability { get; set; } = "Offline";
    public int Capacity { get; set; }
    public int ActiveCases { get; set; }
    public DateTimeOffset? LastAssignedAt { get; set; }
}
