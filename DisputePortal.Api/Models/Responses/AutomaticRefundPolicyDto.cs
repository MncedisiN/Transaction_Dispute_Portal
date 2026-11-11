namespace DisputePortal.Api.Models.Responses;

public class AutomaticRefundPolicyDto : DisputePortal.Platform.Contracts.PolicyVersionMetadata
{
    public bool Enabled { get; set; }
    public decimal MaximumAmount { get; set; }
    public int MaximumAgeHours { get; set; }
    public decimal DailyLimit { get; set; }
    public int DuplicateWindowMinutes { get; set; }

    public AutomaticRefundPolicyDto()
    {
    }

    public AutomaticRefundPolicyDto(int Version, bool Enabled, decimal MaximumAmount, int MaximumAgeHours, decimal DailyLimit)
    {
        this.Version = Version;
        this.Enabled = Enabled;
        this.MaximumAmount = MaximumAmount;
        this.MaximumAgeHours = MaximumAgeHours;
        this.DailyLimit = DailyLimit;
    }
}
