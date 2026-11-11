namespace DisputePortal.Api.Models.Internal;

public class NotificationDelivery
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? DisputeId { get; set; }
    public string Milestone { get; set; } = "";
    public string Channel { get; set; } = "";
    public int Attempts { get; set; }
}
