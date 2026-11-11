using DisputePortal.Platform.Contracts;
using DisputePortal.Platform.Infrastructure.Interfaces;
using DisputePortal.Platform.Notifications.Interfaces;

namespace DisputePortal.Platform.Notifications;

public class NotificationConsumer(INotificationStore store) : IDomainEventConsumer
{
    // Drain already-bound previously bound events safely; Request suppresses account/payment notifications before inserting anything.
    public string[] Subscriptions => [EventTypes.TransactionCompleted, EventTypes.TransactionFailed, EventTypes.AccountActivated, "transaction.received"];

    public Task Handle(DomainEvent e, CancellationToken ct) => store.Request(e, ct);
}
