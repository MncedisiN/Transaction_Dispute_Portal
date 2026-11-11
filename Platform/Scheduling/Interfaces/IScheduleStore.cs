using DisputePortal.Platform.Contracts;

namespace DisputePortal.Platform.Scheduling.Interfaces;

public interface IScheduleStore
{
    Task<object> ScheduleReversalReview(ScheduleReversal request, Guid actor, CancellationToken ct);

    Task QueueDueReversalPreparations(CancellationToken ct);

    Task<object> GetScheduledReversalReviews(CancellationToken ct);
}
