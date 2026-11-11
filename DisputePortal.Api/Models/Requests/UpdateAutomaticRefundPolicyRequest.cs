namespace DisputePortal.Api.Models.Requests;

public class UpdateAutomaticRefundPolicyRequest : PolicyPublicationRequest
{
    public int ExpectedVersion { get; set; }
    public bool Enabled { get; set; }
    public decimal MaximumAmount { get; set; }
    public int MaximumAgeHours { get; set; }
    public decimal DailyLimit { get; set; }
    // Omitted by older clients: inherit the effective database policy, preserving old request fingerprints.
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public int? DuplicateWindowMinutes { get; set; }

    public UpdateAutomaticRefundPolicyRequest()
    {
    }

    public UpdateAutomaticRefundPolicyRequest(
        int ExpectedVersion,
        bool Enabled,
        decimal MaximumAmount,
        int MaximumAgeHours,
        decimal DailyLimit,
        string Reason)
    {
        this.ExpectedVersion = ExpectedVersion;
        ConcurrencyToken = ExpectedVersion;
        RequestId = Guid.NewGuid();
        this.Enabled = Enabled;
        this.MaximumAmount = MaximumAmount;
        this.MaximumAgeHours = MaximumAgeHours;
        this.DailyLimit = DailyLimit;
        this.Reason = Reason;
    }
}
