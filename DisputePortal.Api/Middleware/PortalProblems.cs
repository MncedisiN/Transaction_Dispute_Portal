using Microsoft.AspNetCore.Mvc;

namespace DisputePortal.Api.Middleware;

internal static class PortalProblems
{
    internal static void Customize(ProblemDetailsContext context)
    {
        context.ProblemDetails.Extensions["correlationId"] = context.HttpContext.Items["CorrelationId"]?.ToString() ?? context.HttpContext.TraceIdentifier;
     
    }

    internal static async Task WriteSanitizedProblemDetails(
        HttpContext context,
        IProblemDetailsService service,
        ProblemDetails problem,
        CancellationToken ct)
    {
        var details = new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = problem
        };
        Customize(details);
        if (!await service.TryWriteAsync(details))
        {
            await context.Response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json", cancellationToken: ct);
        }
    }
}
