using System.ComponentModel.DataAnnotations;

namespace DisputePortal.Api.Models.Requests;

public class SignInRequest
{
    [Required]
    public string LoginIdentifier { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;

    public SignInRequest()
    {
    }

    public SignInRequest(string LoginIdentifier, string Password)
    {
        this.LoginIdentifier = LoginIdentifier;
        this.Password = Password;
    }
}
