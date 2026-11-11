namespace DisputePortal.Api.Models.Domain;

public class LoginResend
{
    public Guid UserId { get; set; }
    public string Channel { get; set; } = string.Empty;
    public string Destination { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public int Revision { get; set; }
    public LoginFlowInfo Flow { get; set; } = null!;

    public LoginResend()
    {
    }

    public LoginResend(Guid UserId, string Channel, string Destination, DateTimeOffset ExpiresAt, int Revision, LoginFlowInfo Flow)
    {
        this.UserId = UserId;
        this.Channel = Channel;
        this.Destination = Destination;
        this.ExpiresAt = ExpiresAt;
        this.Revision = Revision;
        this.Flow = Flow;
    }
}
