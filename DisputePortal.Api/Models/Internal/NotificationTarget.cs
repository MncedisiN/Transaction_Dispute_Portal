namespace DisputePortal.Api.Models.Internal;

public class NotificationTarget
{
    public string Channel { get; set; } = string.Empty;
    public string? Destination { get; set; }
    public bool Enabled { get; set; }

    public NotificationTarget()
    {
    }

    public NotificationTarget(string Channel, string? Destination, bool Enabled)
    {
        this.Channel = Channel;
        this.Destination = Destination;
        this.Enabled = Enabled;
    }
}
