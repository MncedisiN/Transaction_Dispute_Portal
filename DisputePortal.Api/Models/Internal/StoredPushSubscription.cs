namespace DisputePortal.Api.Models.Internal;

public class StoredPushSubscription
{
    public Guid Id { get; set; }
    public string Endpoint { get; set; } = string.Empty;
    public string P256dh { get; set; } = string.Empty;
    public string Auth { get; set; } = string.Empty;

    public StoredPushSubscription()
    {
    }

    public StoredPushSubscription(Guid Id, string Endpoint, string P256dh, string Auth)
    {
        this.Id = Id;
        this.Endpoint = Endpoint;
        this.P256dh = P256dh;
        this.Auth = Auth;
    }

    public StoredPushSubscription(StoredPushSubscription other)
    {
        Id = other.Id;
        Endpoint = other.Endpoint;
        P256dh = other.P256dh;
        Auth = other.Auth;
    }
}
