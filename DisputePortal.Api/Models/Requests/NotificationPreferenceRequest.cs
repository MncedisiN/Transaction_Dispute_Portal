using System.ComponentModel.DataAnnotations;

namespace DisputePortal.Api.Models.Requests;

public class NotificationPreferenceRequest
{
    [Required, RegularExpression("^(inapp|email|sms|whatsapp)$")]
    public string Channel { get; set; } = string.Empty;

    public NotificationPreferenceRequest()
    {
    }

    public NotificationPreferenceRequest(string Channel)
    {
        this.Channel = Channel;
    }
}
