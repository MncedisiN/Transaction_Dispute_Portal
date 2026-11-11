using DisputePortal.Api.Configuration;
using DisputePortal.Api.Interfaces.Repositories;

namespace DisputePortal.Api.Service.BackgroundJobs;

public class AutomaticRefundWorker(IServiceScopeFactory scopes, ILogger<AutomaticRefundWorker> logger, ApiRuntimeOptions options) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.AutomaticRefundIntervalSeconds));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope();
                var finance = scope.ServiceProvider.GetRequiredService<IFinanceRepository>();
                for (var i = 0; i < options.AutomaticRefundBatchSize && await finance.ProcessAutomatic(stoppingToken); i++)
                {
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Automatic dispute processing failed; no uncommitted financial posting is retained");
            }
        }
    }
}
