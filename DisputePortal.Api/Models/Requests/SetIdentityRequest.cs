using System.ComponentModel.DataAnnotations;

namespace DisputePortal.Api.Models.Requests;

public class SetIdentityRequest
{
    [Required, RegularExpression("^[0-9]{13}$")]
    public string IdentityNumber { get; set; } = string.Empty;
    public int BirthCentury { get; set; }

    [Required, MaxLength(128)]
    public string CurrentPassword { get; set; } = string.Empty;

    public SetIdentityRequest()
    {
    }

    public SetIdentityRequest(string IdentityNumber, int BirthCentury, string CurrentPassword)
    {
        this.IdentityNumber = IdentityNumber;
        this.BirthCentury = BirthCentury;
        this.CurrentPassword = CurrentPassword;
    }
}
