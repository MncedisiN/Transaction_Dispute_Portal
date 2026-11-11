namespace DisputePortal.Api.Models.Responses;

public class SecurityActivityDto
{
    public Guid Id { get; set; }
    public Guid? UserId { get; set; }
    public string EventType { get; set; } = "";
    public string Outcome { get; set; } = "";
    public string IpAddress { get; set; } = "";
    public string DeviceType { get; set; } = "";
    public string Browser { get; set; } = "";
    public string Location { get; set; } = "";
    public DateTimeOffset OccurredAt { get; set; }
}
