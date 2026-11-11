namespace DisputePortal.Api.Models.Responses;

public class SignInDetailDto
{
    public Guid Id { get; set; }
    public string EventType { get; set; } = "";
    public string Outcome { get; set; } = "";
    public string IpAddress { get; set; } = "";
    public string DeviceType { get; set; } = "";
    public string Browser { get; set; } = "";
    public string UserAgent { get; set; } = "";
    public string Location { get; set; } = "";
    public DateTimeOffset OccurredAt { get; set; }
    public Guid? SessionId { get; set; }
    public bool SessionActive { get; set; }
    public bool IsCurrentSession { get; set; }
}
