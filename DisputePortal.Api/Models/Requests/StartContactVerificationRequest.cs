using System.ComponentModel.DataAnnotations;

namespace DisputePortal.Api.Models.Requests;

public class StartContactVerificationRequest
{
    [Required, RegularExpression("^(email|phone)$")]
    public string Field { get; set; } = string.Empty;

    [Required, MaxLength(320)]
    public string Destination { get; set; } = string.Empty;

    [RegularExpression("^[A-Z]{2}$"), MaxLength(2)]
    public string? PhoneCountry { get; set; }

    [Required, RegularExpression("^(email|sms|whatsapp)$")]
    public string Channel { get; set; } = string.Empty;

    [Required, MaxLength(128)]
    public string CurrentPassword { get; set; } = string.Empty;

    public StartContactVerificationRequest()
    {
    }

    public StartContactVerificationRequest(string Field, string Destination, string Channel, string CurrentPassword)
    {
        this.Field = Field;
        this.Destination = Destination;
        this.Channel = Channel;
        this.CurrentPassword = CurrentPassword;
    }
}
