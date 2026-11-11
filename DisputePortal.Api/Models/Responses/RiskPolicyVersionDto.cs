using DisputePortal.Platform.Contracts;

namespace DisputePortal.Api.Models.Responses;

public class RiskPolicyVersionDto : PolicyVersionMetadata
{
    public bool Enabled { get; set; } = true;
    public decimal HighValueThreshold { get; set; }
    public int ExtraCheckAfterDays { get; set; }
    public int DebitOrderWindowDays { get; set; }
    public decimal DebitOrderAppLimit { get; set; }
}
