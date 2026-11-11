using System.ComponentModel.DataAnnotations;

namespace DisputePortal.Api.Models.Requests;

public class VerifyIdentityRequest
{
    public Guid ChallengeId { get; set; }

    [Required, RegularExpression("^[0-9]{6}$")]
    public string Code { get; set; } = string.Empty;

    public VerifyIdentityRequest()
    {
    }

    public VerifyIdentityRequest(Guid ChallengeId, string Code)
    {
        this.ChallengeId = ChallengeId;
        this.Code = Code;
    }
}
