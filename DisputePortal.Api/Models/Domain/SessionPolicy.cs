namespace DisputePortal.Api.Models.Domain;

public class SessionPolicy
{
    public int IdleMinutes { get; set; } = 15;
    public int AbsoluteMinutes { get; set; } = 30;

    public void ValidateSessionPolicy()
    {
        if (IdleMinutes is < 1 or > 60 || AbsoluteMinutes < IdleMinutes || AbsoluteMinutes > 480)
        {
            throw new InvalidOperationException("Session policy requires 1–60 idle minutes and an absolute lifetime between the idle limit and 480 minutes.");
        }
    }
}
