namespace DisputePortal.Api.Models.Domain;

public class PushDeliveryResult
{
    public string Outcome { get; set; } = string.Empty;
    public string? Code { get; set; }
    public int Sent { get; set; }
    public int Unavailable { get; set; }

    public PushDeliveryResult()
    {
    }

    public PushDeliveryResult(string Outcome, string? Code, int Sent, int Unavailable)
    {
        this.Outcome = Outcome;
        this.Code = Code;
        this.Sent = Sent;
        this.Unavailable = Unavailable;
    }
}
