using System.ComponentModel.DataAnnotations;

namespace DisputePortal.Api.Models.Requests;

public class CompleteContactVerificationRequest
{
    public Guid ChallengeId { get; set; }

    [Required, RegularExpression("^[0-9]{6}$")]
    public string Code { get; set; } = string.Empty;

    [Required, MaxLength(128)]
    public string CurrentPassword { get; set; } = string.Empty;

    public CompleteContactVerificationRequest()
    {
    }

    public CompleteContactVerificationRequest(Guid ChallengeId, string Code, string CurrentPassword)
    {
        this.ChallengeId = ChallengeId;
        this.Code = Code;
        this.CurrentPassword = CurrentPassword;
    }
}
