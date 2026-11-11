using System.ComponentModel.DataAnnotations;

namespace DisputePortal.Api.Models.Requests;

public class ResetPasswordRequest
{
    public Guid ChallengeId { get; set; }

    [Required, RegularExpression("^[0-9]{6}$")]
    public string Code { get; set; } = string.Empty;

    [Required, MinLength(12), MaxLength(128)]
    public string Password { get; set; } = string.Empty;

    public ResetPasswordRequest()
    {
    }

    public ResetPasswordRequest(Guid ChallengeId, string Code, string Password)
    {
        this.ChallengeId = ChallengeId;
        this.Code = Code;
        this.Password = Password;
    }
}
