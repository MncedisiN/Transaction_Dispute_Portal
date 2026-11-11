namespace DisputePortal.Api.Interfaces.Services; 
public interface IRealtimePublisher
{
    Task PublishPortalUpdate(
        IReadOnlyCollection<Guid> userIds,
        bool notifyStaff,
        IReadOnlyCollection<string> topics,
        Guid? resourceId,
        CancellationToken cancellationToken);
}
