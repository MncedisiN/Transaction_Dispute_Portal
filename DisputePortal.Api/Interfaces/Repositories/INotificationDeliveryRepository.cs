using DisputePortal.Api.Models.Internal;
using DisputePortal.Api.Models.Requests;

namespace DisputePortal.Api.Interfaces.Repositories;

public interface INotificationDeliveryRepository
{
    Task<string> GetNotificationPreference(Guid user, CancellationToken ct);

    Task SaveNotificationPreference(Guid user, string channel, CancellationToken ct);

    Task SavePushSubscription(Guid user, Guid session, PushSubscriptionRequest subscription, CancellationToken ct);

    Task RemovePushSubscriptions(Guid user, Guid session, CancellationToken ct);

    Task<IReadOnlyList<StoredPushSubscription>> GetPushSubscriptions(Guid user, CancellationToken ct, Guid? session = null);

    Task DeletePushSubscription(Guid id, CancellationToken ct);

    Task<IReadOnlyList<NotificationDelivery>> ClaimNotifications(CancellationToken ct);

    Task<NotificationTarget> GetNotificationTarget(Guid user, string channel, CancellationToken ct);

    Task<long> GetUnreadNotificationCount(Guid user, CancellationToken ct) => Task.FromResult(0L);

    Task<PushNotificationContext?> GetPushContext(Guid user, Guid delivery, CancellationToken ct) =>
        Task.FromResult<PushNotificationContext?>(null);

    Task CompleteNotification(Guid id, string outcome, string? error, CancellationToken ct);
}
