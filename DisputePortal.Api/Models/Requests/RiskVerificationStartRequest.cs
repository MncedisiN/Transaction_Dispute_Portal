using System.ComponentModel.DataAnnotations;

namespace DisputePortal.Api.Models.Requests;

public class RiskVerificationStartRequest
{
    [Required, StringLength(128)]
    public string CurrentPassword { get; set; } = string.Empty;

    public RiskVerificationStartRequest()
    {
    }

    public RiskVerificationStartRequest(string CurrentPassword)
    {
        this.CurrentPassword = CurrentPassword;
    }
}
