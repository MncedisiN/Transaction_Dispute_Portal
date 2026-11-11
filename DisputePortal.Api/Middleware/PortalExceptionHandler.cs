using DisputePortal.Api.Models.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace DisputePortal.Api.Middleware;

public class PortalExceptionHandler(
    ILogger<PortalExceptionHandler> logger,
    IProblemDetailsService problems,
    IHostEnvironment environment,
    Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.HttpsPolicy.HstsOptions> hstsOptions,
    ILoggerFactory loggerFactory) : IExceptionHandler
{
    private readonly Microsoft.AspNetCore.HttpsPolicy.HstsMiddleware hsts = new(_ => Task.CompletedTask, hstsOptions, loggerFactory);

    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        if (context.Response.HasStarted || exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested)
        {
            return false;
        }

        var (status, title, detail) = exception switch
        {
            DisputePortal.Platform.Infrastructure.Models.PlatformException e when e.Status is >= 400 and < 500 => (e.Status, "Financial service", e.Message),
            DisputePortal.Platform.Infrastructure.Models.PlatformException e when e.Status == 503 => (503, "Service unavailable", "This service is temporarily unavailable. Please try again shortly."),
            BadHttpRequestException e => (e.StatusCode, "Invalid request", "The request format or size is not supported."),
            PasswordSafetyUnavailableException e => (503, "Password checks unavailable", e.Message),
            EvidenceScanUnavailableException e => (503, "Evidence checks unavailable", e.Message),
            VerificationUnavailableException e => (503, "Verification unavailable", e.Message),
            PortalForbiddenException e => (403, "Access denied", e.Message),
            PortalNotFoundException => (404, "Not found", "The requested record was not found."),
            PortalConflictException e => (409, "Conflict", e.Message),
            PortalValidationException e => (422, "Validation failed", e.Message),
            _ => (500, "Request failed", "This action is temporarily unavailable. Your information remains protected. Please try again shortly.")
        }; 
        if (status >= 500)
        {
            logger.LogError(exception, "Request failed with {StatusCode} ({ExceptionType})", status, exception.GetType().Name);
        }
         
        if (!environment.IsDevelopment())
        {
            await hsts.Invoke(context);
        }

        context.Response.StatusCode = status;
        await PortalProblems.WriteSanitizedProblemDetails(
            context,
            problems,
            new ProblemDetails
            {
                Status = status,
                Title = title,
                Detail = detail
            },
            cancellationToken);
        return true;
    }
}
