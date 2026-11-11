using DisputePortal.Api.Models.Responses;

namespace DisputePortal.Api.Models.Domain;

public class SignInSessionAccess
{
    public CurrentUserDto? User { get; set; }
    public bool VerificationRequired { get; set; }
    public bool CanRememberDevice { get; set; }
}
