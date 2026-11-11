using System.ComponentModel.DataAnnotations;

namespace DisputePortal.Api.Models.Requests;

public class RecoveryRequest
{
    [Required, MaxLength(320)]
    public string Contact { get; set; } = string.Empty;

    [Required, RegularExpression("^(email|sms|whatsapp)$")]
    public string Channel { get; set; } = string.Empty;

    public RecoveryRequest()
    {
    }

    public RecoveryRequest(string Contact, string Channel)
    {
        this.Contact = Contact;
        this.Channel = Channel;
    }
}
