namespace DisputePortal.Platform.Notifications.Models;

public class NotificationItem
{
    public Guid Id { get; set; }
    public Guid AggregateId { get; set; }
    public string TemplateKey { get; set; } = "";
    public string Channel { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ReadAt { get; set; }

    public NotificationItem()
    {
    }

    public NotificationItem(
        Guid id,
        Guid aggregateId,
        string templateKey,
        string channel,
        string status,
        DateTimeOffset createdAt,
        DateTimeOffset? readAt)
    {
        Id = id;
        AggregateId = aggregateId;
        TemplateKey = templateKey;
        Channel = channel;
        Status = status;
        CreatedAt = createdAt;
        ReadAt = readAt;
    }
}
