using DisputePortal.Api.Configuration;
using DisputePortal.Api.Interfaces.Repositories;

namespace DisputePortal.Api.Service.BackgroundJobs;

public class AssignmentWorker(IServiceScopeFactory scopes, ILogger<AssignmentWorker> logger, ApiRuntimeOptions options) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.AssignmentIntervalSeconds));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<IOperationsRepository>().AssignWaitingCases(stoppingToken);
                await scope.ServiceProvider.GetRequiredService<ISupportRepository>().AssignWaitingSupportConversations(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e)
            {
                logger.LogError(e, "Assignment cycle failed; pending cases remain queued.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
