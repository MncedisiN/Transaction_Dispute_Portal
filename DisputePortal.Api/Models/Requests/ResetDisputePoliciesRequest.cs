using System.ComponentModel.DataAnnotations;

namespace DisputePortal.Api.Models.Requests;

public class ResetDisputePoliciesRequest
{
    public Guid RequestId { get; set; }
    public long? RiskConcurrencyToken { get; set; }
    public long? AutomaticConcurrencyToken { get; set; }
    public int ExpectedRiskVersion { get; set; }
    public int ExpectedAutomaticVersion { get; set; }

    [Required, StringLength(500, MinimumLength = 10)]
    public string Reason { get; set; } = string.Empty;

    public ResetDisputePoliciesRequest()
    {
    }

    public ResetDisputePoliciesRequest(int ExpectedRiskVersion, int ExpectedAutomaticVersion, string Reason)
    {
        RequestId = Guid.NewGuid();
        this.ExpectedRiskVersion = ExpectedRiskVersion;
        this.ExpectedAutomaticVersion = ExpectedAutomaticVersion;
        this.Reason = Reason;
    }

    public ResetDisputePoliciesRequest(ResetDisputePoliciesRequest other)
    {
        RequestId = other.RequestId;
        RiskConcurrencyToken = other.RiskConcurrencyToken;
        AutomaticConcurrencyToken = other.AutomaticConcurrencyToken;
        ExpectedRiskVersion = other.ExpectedRiskVersion;
        ExpectedAutomaticVersion = other.ExpectedAutomaticVersion;
        Reason = other.Reason;
    }
}
