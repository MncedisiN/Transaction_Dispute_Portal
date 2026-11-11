using DisputePortal.Api.Models.Internal;

namespace DisputePortal.Api.Interfaces.Push;

public interface IPushTransport
{
    Task<PushAttempt> SendEncryptedWebPush(StoredPushSubscription subscription, string payload, CancellationToken ct);
}
