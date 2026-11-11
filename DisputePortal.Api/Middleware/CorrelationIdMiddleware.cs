using System.Diagnostics;

namespace DisputePortal.Api.Middleware;

public class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    public async Task Invoke(HttpContext context)
    {
        var supplied = context.Request.Headers["X-Correlation-ID"];
        var id = supplied.Count == 1 && supplied[0] is { Length: 36 } text && Guid.TryParseExact(text, "D", out var parsed) ? parsed.ToString("D") : Guid.NewGuid().ToString("D");
        context.Items["CorrelationId"] = id;
        if (!context.Response.HasStarted)
        {
            context.Response.OnStarting(() =>
            {
                context.Response.Headers["X-Correlation-ID"] = id;
                return Task.CompletedTask;
            });
        }

        Activity.Current?.SetTag("correlation.id", id);
        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = id }))
            await next(context);
    }
}
