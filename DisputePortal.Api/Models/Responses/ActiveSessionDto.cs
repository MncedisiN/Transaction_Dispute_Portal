namespace DisputePortal.Api.Models.Responses;

public class ActiveSessionDto
{
    public Guid Id { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public string? IpAddress { get; set; }
    public string? DeviceType { get; set; }
    public string? Browser { get; set; }
}
