using System.ComponentModel.DataAnnotations;

namespace DisputePortal.Api.Models.Requests;

public class GrantEvidenceAccessRequest
{
    public Guid UserId { get; set; }
    public int Hours { get; set; }

    [Required]
    public string Reason { get; set; } = string.Empty;

    public GrantEvidenceAccessRequest()
    {
    }

    public GrantEvidenceAccessRequest(Guid UserId, int Hours, string Reason)
    {
        this.UserId = UserId;
        this.Hours = Hours;
        this.Reason = Reason;
    }
}
