using DisputePortal.Api.Models.Domain;

namespace DisputePortal.Api.Models.Responses;

public class PolicyDefaultsDto
{
    public string Provenance { get; set; } = "UnpublishedResetPreset";
    public bool IsPublished { get; set; }
    public DisputeRiskPolicy Submission { get; set; } = null!;
    public AutomaticRefundPolicyDto Automatic { get; set; } = null!;
    public PolicyLimitsDto Limits { get; set; } = null!;

    public PolicyDefaultsDto()
    {
    }

    public PolicyDefaultsDto(DisputeRiskPolicy Submission, AutomaticRefundPolicyDto Automatic, PolicyLimitsDto Limits)
    {
        this.Submission = Submission;
        this.Automatic = Automatic;
        this.Limits = Limits;
    }
}
