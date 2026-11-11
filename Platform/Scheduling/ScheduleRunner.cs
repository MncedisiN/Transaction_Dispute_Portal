using DisputePortal.Platform.Infrastructure.Interfaces;
using DisputePortal.Platform.Scheduling.Interfaces;

namespace DisputePortal.Platform.Scheduling;

public class ScheduleRunner(IScheduleStore store) : IPersistentJobRunner
{
    public Task RunScheduledWork(CancellationToken ct) => store.QueueDueReversalPreparations(ct);
}
