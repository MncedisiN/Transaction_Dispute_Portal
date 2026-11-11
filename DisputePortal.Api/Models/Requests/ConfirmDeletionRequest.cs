using System.ComponentModel.DataAnnotations;

namespace DisputePortal.Api.Models.Requests;

public class ConfirmDeletionRequest
{
    public Guid ChallengeId { get; set; }

    [Required, RegularExpression("^[0-9]{6}$")]
    public string Code { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Reason { get; set; }

    public ConfirmDeletionRequest()
    {
    }

    public ConfirmDeletionRequest(Guid ChallengeId, string Code, string? Reason)
    {
        this.ChallengeId = ChallengeId;
        this.Code = Code;
        this.Reason = Reason;
    }
}
