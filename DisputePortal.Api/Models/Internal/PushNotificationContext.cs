namespace DisputePortal.Api.Models.Internal;

public class PushNotificationContext
{
    public Guid? DisputeId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public long UnreadCount { get; set; }
    public string? Body { get; set; }

    public PushNotificationContext()
    {
    }

    public PushNotificationContext(string Kind, string Url, long UnreadCount, string? Body = null)
    {
        this.Kind = Kind;
        this.Url = Url;
        this.UnreadCount = UnreadCount;
        this.Body = Body;
    }
}
