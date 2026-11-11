using System.ComponentModel.DataAnnotations;

namespace DisputePortal.Api.Models.Requests;

public class LoginVerificationSettingsRequest
{
    [Required]
    public bool? Enabled { get; set; }

    [Required, StringLength(128, MinimumLength = 1)]
    public string CurrentPassword { get; set; } = string.Empty;

    public LoginVerificationSettingsRequest()
    {
    }

    public LoginVerificationSettingsRequest(bool? Enabled, string CurrentPassword)
    {
        this.Enabled = Enabled;
        this.CurrentPassword = CurrentPassword;
    }
}
