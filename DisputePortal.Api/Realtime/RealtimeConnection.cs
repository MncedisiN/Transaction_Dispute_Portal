namespace DisputePortal.Api.Realtime;

public class RealtimeConnection
{
    public string ConnectionId { get; set; } = null!;
    public Guid UserId { get; set; }
    public Guid? SessionId { get; set; }
    public string Permissions { get; set; } = null!;
    public Action Abort { get; set; } = null!;
}
