using System.ComponentModel.DataAnnotations;

namespace DisputePortal.Api.Models.Requests;

public class PushSubscriptionRequest
{
    [Required, MaxLength(2048)]
    public string Endpoint { get; set; } = string.Empty;

    [Required, MaxLength(256)]
    public string P256dh { get; set; } = string.Empty;

    [Required, MaxLength(128)]
    public string Auth { get; set; } = string.Empty;

    public PushSubscriptionRequest()
    {
    }

    public PushSubscriptionRequest(string Endpoint, string P256dh, string Auth)
    {
        this.Endpoint = Endpoint;
        this.P256dh = P256dh;
        this.Auth = Auth;
    }
}
