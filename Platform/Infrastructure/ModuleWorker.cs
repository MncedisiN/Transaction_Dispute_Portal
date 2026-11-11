using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using DisputePortal.Platform.Infrastructure.Interfaces;

namespace DisputePortal.Platform.Infrastructure;

public class ModuleWorker(IEnumerable<IPersistentJobRunner> jobs, ILogger<ModuleWorker> logger, ServiceOptions options) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            foreach (var job in jobs)
            {
                try
                {
                    ct.ThrowIfCancellationRequested();
                    await job.RunScheduledWork(ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception error)
                {
                    logger.LogWarning(
                        error,
                        "{Operation} deferred after {ErrorType}",
                        "Service.Maintenance." + job.GetType().Name,
                        error.GetType().Name);
                }
            }

            await Task.Delay(options.MaintenancePollMilliseconds, ct);
        }
    }
}
