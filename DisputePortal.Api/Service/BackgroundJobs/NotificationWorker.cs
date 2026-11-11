using DisputePortal.Api.Configuration;
using DisputePortal.Api.Service.Push;

namespace DisputePortal.Api.Service.BackgroundJobs;

public class NotificationWorker(IServiceScopeFactory scopes, ILogger<NotificationWorker> logger, ApiRuntimeOptions options) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.NotificationIntervalSeconds));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    using var scope = scopes.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<NotificationDeliveryService>().Process(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception error)
                {
                    logger.LogWarning("Notification queue processing deferred ({FailureType})", error.GetType().Name);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
