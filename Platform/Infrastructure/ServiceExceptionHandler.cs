using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using DisputePortal.Platform.Infrastructure.Models;

namespace DisputePortal.Platform.Infrastructure;

public class ServiceExceptionHandler(ILogger<ServiceExceptionHandler> logger, IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception error, CancellationToken ct)
    {
        if (context.Response.HasStarted || error is OperationCanceledException && context.RequestAborted.IsCancellationRequested)
        {
            return false;
        }

        var known = error as PlatformException;
        var status = known?.Status ?? StatusCodes.Status503ServiceUnavailable;
        if (status is < 400 or > 599)
        {
            status = StatusCodes.Status503ServiceUnavailable;
        }

        var code = known?.Code ?? "service_unavailable";
        var message = status < 500 && known is not null ? known.Message : "This operation is temporarily unavailable.";
        if (status >= 500)
        {
            logger.LogError(error, "Service operation failed with {StatusCode} ({ExceptionType})", status, error.GetType().Name);
        }

        context.Response.StatusCode = status;
        var details = new ProblemDetails
        {
            Status = status,
            Title = status < 500 ? "Operation rejected" : "Service unavailable",
            Detail = message,
            Extensions =
            {
                ["code"] = code,
                ["message"] = message,
                ["correlationId"] = context.Items["CorrelationId"]?.ToString() ?? context.TraceIdentifier
            }
        };
        if (!await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = details
        }))
        {
            await context.Response.WriteAsJsonAsync(details, options: null, contentType: "application/problem+json", cancellationToken: ct);
        }

        return true;
    }
}
