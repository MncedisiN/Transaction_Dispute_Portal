using DisputePortal.Api.Models.Domain;

namespace DisputePortal.Api.Interfaces.Push;

public interface IPushDeliveryService
{
    Task<PushDeliveryResult> SendPushNotification(Guid user, string milestone, Guid deliveryId, CancellationToken ct, Guid? session = null);
}
