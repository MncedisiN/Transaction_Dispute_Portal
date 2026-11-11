using System.Diagnostics;
using DisputePortal.Api.Interfaces.Observability;

namespace DisputePortal.Api.Observability;

public class MetricPersistenceWorker(
    MetricAggregator aggregation,
    IApiMetricStore store,
    TelemetryOptions options,
    TimeProvider clock,
    PortalTelemetry telemetry,
    ILogger<MetricPersistenceWorker> logger) : BackgroundService
{
    private MetricBatch? pending;
    private DateTimeOffset nextPrune;

    public async Task<bool> FlushOnce(CancellationToken ct)
    {
        pending ??= aggregation.TakeBatch();
        if (pending is null)
        {
            return true;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.StorageTimeoutSeconds));
        using var activity = telemetry.Activities.StartActivity("telemetry.persist", ActivityKind.Internal);
        activity?.SetTag("telemetry.summary.count", pending.Summaries.Count);
        try
        {
            await store.Persist(pending, timeout.Token);
            logger.LogInformation("API metric summaries persisted. Rows {Rows}", pending.Summaries.Count);
            pending = null;
            telemetry.BatchPersisted();
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error)
        {
            telemetry.StorageFailed();
            activity?.SetStatus(ActivityStatusCode.Error);
            // Keep this exact batch ID for idempotent retry after uncertain commits.
            logger.LogWarning(
                "API metric storage unavailable; bounded aggregates retained. ExceptionType {ExceptionType}, BufferedSeries {BufferedSeries}, Dropped {Dropped}",
                error.GetType().Name,
                aggregation.BufferedSeries,
                aggregation.DroppedMeasurements);
            return false;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.FlushSeconds), clock);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                for (var i = 0; i < 16; i++)
                    if (!await FlushOnce(stoppingToken) || aggregation.BufferedSeries == 0)
                    {
                        break;
                    }

                if (clock.GetUtcNow() >= nextPrune)
                {
                    nextPrune = clock.GetUtcNow().AddHours(1);
                    try
                    {
                        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                        timeout.CancelAfter(TimeSpan.FromSeconds(options.StorageTimeoutSeconds));
                        await store.Prune(clock.GetUtcNow().AddDays(-options.RetentionDays), timeout.Token);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception error)
                    {
                        logger.LogWarning("API metric retention deferred. ExceptionType {ExceptionType}", error.GetType().Name);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    public override async Task StopAsync(CancellationToken ct)
    {
        await base.StopAsync(ct);
        using var final = CancellationTokenSource.CreateLinkedTokenSource(ct);
        final.CancelAfter(TimeSpan.FromSeconds(options.StorageTimeoutSeconds));
        try
        {
            for (var i = 0; i < 16; i++)
                if (!await FlushOnce(final.Token) || aggregation.BufferedSeries == 0)
                {
                    break;
                }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
