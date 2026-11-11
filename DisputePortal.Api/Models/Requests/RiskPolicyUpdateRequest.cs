namespace DisputePortal.Api.Models.Requests;

public class RiskPolicyUpdateRequest : PolicyPublicationRequest
{
    public bool Enabled { get; set; } = true;
    public int ExpectedVersion { get; set; }
    public decimal HighValueThreshold { get; set; }
    public int ExtraCheckAfterDays { get; set; }
    public int DebitOrderWindowDays { get; set; }
    public decimal DebitOrderAppLimit { get; set; }

    public RiskPolicyUpdateRequest()
    {
    }

    public RiskPolicyUpdateRequest(
        int ExpectedVersion,
        decimal HighValueThreshold,
        int ExtraCheckAfterDays,
        int DebitOrderWindowDays,
        decimal DebitOrderAppLimit,
        string Reason)
    {
        this.ExpectedVersion = ExpectedVersion;
        ConcurrencyToken = ExpectedVersion;
        RequestId = Guid.NewGuid();
        this.HighValueThreshold = HighValueThreshold;
        this.ExtraCheckAfterDays = ExtraCheckAfterDays;
        this.DebitOrderWindowDays = DebitOrderWindowDays;
        this.DebitOrderAppLimit = DebitOrderAppLimit;
        this.Reason = Reason;
    }
}
