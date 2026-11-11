using DisputePortal.Platform.Infrastructure.Interfaces;
using DisputePortal.Platform.Notifications.Interfaces;

namespace DisputePortal.Platform.Notifications;

public class InAppDelivery(INotificationStore store) : IPersistentJobRunner
{
    public Task RunScheduledWork(CancellationToken ct) => store.Deliver(ct);
}
