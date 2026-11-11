using DisputePortal.Api.Models.Domain;

namespace DisputePortal.Api.Models.Responses;

public class PolicyConfigurationDto
{
    public DisputeRiskPolicy Submission { get; set; } = null!;
    public AutomaticRefundPolicyDto Automatic { get; set; } = null!;

    public PolicyConfigurationDto()
    {
    }

    public PolicyConfigurationDto(DisputeRiskPolicy Submission, AutomaticRefundPolicyDto Automatic)
    {
        this.Submission = Submission;
        this.Automatic = Automatic;
    }
}
