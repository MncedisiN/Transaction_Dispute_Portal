using System.ComponentModel.DataAnnotations;

namespace DisputePortal.Api.Models.Requests;

public class RiskVerificationCompleteRequest
{
    public Guid ChallengeId { get; set; }

    [Required, RegularExpression("^[0-9]{6}$")]
    public string Code { get; set; } = string.Empty;

    public RiskVerificationCompleteRequest()
    {
    }

    public RiskVerificationCompleteRequest(Guid ChallengeId, string Code)
    {
        this.ChallengeId = ChallengeId;
        this.Code = Code;
    }
}
