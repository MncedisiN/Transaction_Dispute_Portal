using System.ComponentModel.DataAnnotations;

namespace DisputePortal.Api.Models.Requests;

public class RevokeEvidenceAccessRequest
{
    [Required]
    public string Reason { get; set; } = string.Empty;

    public RevokeEvidenceAccessRequest()
    {
    }

    public RevokeEvidenceAccessRequest(string Reason)
    {
        this.Reason = Reason;
    }
}
