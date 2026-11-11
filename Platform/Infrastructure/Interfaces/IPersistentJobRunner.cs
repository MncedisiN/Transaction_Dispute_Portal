namespace DisputePortal.Platform.Infrastructure.Interfaces;

public interface IPersistentJobRunner
{
    Task RunScheduledWork(CancellationToken ct);
}
