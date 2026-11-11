namespace DisputePortal.Platform.Contracts;

public class NotificationEvent
{
    public Guid NotificationId { get; set; }
    public Guid UserId { get; set; }
    public string TemplateKey { get; set; } = null!;
    public Guid AggregateId { get; set; }
    public string Channel { get; set; } = "inapp";
}
