using DisputePortal.Api.Models.Responses;

namespace DisputePortal.Api.Interfaces.Repositories;

public interface INotificationRepository
{
    Task<PageResult<NotificationDto>> GetNotifications(Guid customerId, int page, int pageSize, CancellationToken cancellationToken);

    Task<long> GetUnreadNotificationCount(Guid user, CancellationToken ct) => Task.FromResult(0L);

    Task<bool> MarkNotificationRead(Guid customerId, Guid notificationId, CancellationToken cancellationToken);
}
