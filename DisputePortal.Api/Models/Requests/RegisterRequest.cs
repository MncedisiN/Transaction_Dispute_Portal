using System.ComponentModel.DataAnnotations;

namespace DisputePortal.Api.Models.Requests;

public class RegisterRequest
{
    [Required, MaxLength(100)]
    public string FirstName { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string LastName { get; set; } = string.Empty;

    [Required, MaxLength(320)]
    public string Contact { get; set; } = string.Empty;

    [Required, RegularExpression("^(email|sms|whatsapp)$")]
    public string Channel { get; set; } = string.Empty;

    [Required, MinLength(12), MaxLength(128)]
    public string Password { get; set; } = string.Empty;
    public RegistrationDetails? Details { get; set; }

    public RegisterRequest()
    {
    }

    public RegisterRequest(
        string FirstName,
        string LastName,
        string Contact,
        string Channel,
        string Password,
        RegistrationDetails? Details = null)
    {
        this.FirstName = FirstName;
        this.LastName = LastName;
        this.Contact = Contact;
        this.Channel = Channel;
        this.Password = Password;
        this.Details = Details;
    }
}
