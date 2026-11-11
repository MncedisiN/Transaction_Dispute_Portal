using System.ComponentModel.DataAnnotations;

namespace DisputePortal.Api.Models.Requests;

public class ChangePasswordRequest
{
    [Required, MaxLength(128)]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required, MinLength(12), MaxLength(128)]
    public string NewPassword { get; set; } = string.Empty;

    [Required, MaxLength(128)]
    public string ConfirmPassword { get; set; } = string.Empty;

    public ChangePasswordRequest()
    {
    }

    public ChangePasswordRequest(string CurrentPassword, string NewPassword, string ConfirmPassword)
    {
        this.CurrentPassword = CurrentPassword;
        this.NewPassword = NewPassword;
        this.ConfirmPassword = ConfirmPassword;
    }
}
