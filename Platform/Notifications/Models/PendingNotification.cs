namespace DisputePortal.Platform.Notifications.Models;

internal class PendingNotification
{
    public Guid NotificationId { get; set; }
    public Guid UserId { get; set; }
    public string TemplateKey { get; set; } = "";
    public Guid AggregateId { get; set; }
    public string Channel { get; set; } = "";
    public string RequestBody { get; set; } = "";

    public PendingNotification()
    {
    }

    public PendingNotification(Guid notificationId, Guid userId, string templateKey, Guid aggregateId, string channel, string requestBody)
    {
        NotificationId = notificationId;
        UserId = userId;
        TemplateKey = templateKey;
        AggregateId = aggregateId;
        Channel = channel;
        RequestBody = requestBody;
    }
}
