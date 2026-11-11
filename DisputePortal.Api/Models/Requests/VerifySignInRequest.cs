using System.ComponentModel.DataAnnotations;

namespace DisputePortal.Api.Models.Requests;

public class VerifySignInRequest
{
    public Guid ChallengeId { get; set; }

    [Required, RegularExpression("^[0-9]{6}$")]
    public string Code { get; set; } = string.Empty;
    public bool RememberDevice { get; set; }

    public VerifySignInRequest()
    {
    }

    public VerifySignInRequest(Guid ChallengeId, string Code, bool RememberDevice = false)
    {
        this.ChallengeId = ChallengeId;
        this.Code = Code;
        this.RememberDevice = RememberDevice;
    }
}
