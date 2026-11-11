using DisputePortal.Api.Observability;
using DisputePortal.Platform.Infrastructure.Logging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using DisputePortal.Api.Interfaces.Observability;

namespace DisputePortal.Api.Controllers;

[ApiController]
[Authorize(Policy = "monitoring.read")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("api/admin/monitoring")]
[Route("api/v1/admin/monitoring")]
public class MonitoringController(
    IApiMetricStore store,
    MetricAggregator aggregation,
    TimeProvider clock,
    ILogger<MonitoringController> logger,
    TelemetryOptions options,
    RotatingApplicationLog? logs = null) : ControllerBase
{
    [HttpGet("overview")]
    [EnableRateLimiting("operations-read")]
    public async Task<IActionResult> Overview(DateTimeOffset? from, DateTimeOffset? to, int limit = 100, CancellationToken ct = default)
    {
        var until = (to ?? clock.GetUtcNow()).ToUniversalTime();
        var since = (from ?? until.AddHours(-options.DefaultLookbackHours)).ToUniversalTime();
        if (since >= until
            || until - since > TimeSpan.FromDays(7)
            || until > clock.GetUtcNow().AddMinutes(1)
            || limit is < 1 or > 500)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid monitoring range",
                Detail = "Use a range of up to seven days and a limit between 1 and 500."
            });
        }

        try
        {
            var persisted = await store.Overview(since, until, limit, ct);
            return Ok(new
            {
                persisted,
                collection = new
                {
                    bufferedSeries = aggregation.BufferedSeries,
                    droppedMeasurements = aggregation.DroppedMeasurements,
                    droppedLogEvents = logs?.DroppedEvents ?? 0
                },
                durationUnit = "milliseconds",
                bucketSeconds = 60,
                failureDefinition = "HTTP 5xx or a built-in instrumentation error; HTTP 4xx counted separately"
            });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error)
        {
            logger.LogWarning("Stored monitoring overview unavailable. ExceptionType {ExceptionType}", error.GetType().Name);
            return StatusCode(
                503,
                new ProblemDetails
                {
                    Title = "Monitoring temporarily unavailable",
                    Detail = "Stored monitoring data cannot be read right now."
                });
        }
    }
}
