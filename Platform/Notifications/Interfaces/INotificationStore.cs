using DisputePortal.Platform.Contracts;

namespace DisputePortal.Platform.Notifications.Interfaces;

public interface INotificationStore
{
    Task Request(DomainEvent value, CancellationToken ct);

    Task Deliver(CancellationToken ct);

    Task<object> GetCustomerNotifications(Guid user, CancellationToken ct);

    Task MarkNotificationRead(Guid id, Guid user, CancellationToken ct);
}
