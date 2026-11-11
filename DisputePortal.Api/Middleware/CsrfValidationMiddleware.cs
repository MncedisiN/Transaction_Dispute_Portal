using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;

namespace DisputePortal.Api.Middleware; 
public class CsrfValidationMiddleware(RequestDelegate next)
{
    public async Task Invoke(HttpContext context, IProblemDetailsService problems, IAntiforgery antiforgery)
    {
        var feature = context.Features.Get<IAntiforgeryValidationFeature>();
        var invalid = feature is { IsValid: false };
        var method = context.Request.Method; 
        var unsafeMethod = !HttpMethods.IsGet(method)
            && !HttpMethods.IsHead(method)
            && !HttpMethods.IsOptions(method)
            && !HttpMethods.IsTrace(method);
        var websocket = context.Request.Path == DisputePortal.Api.Realtime.PortalRealtimeHub.EndpointPath
            && context.WebSockets.IsWebSocketRequest;
        if (feature is null
            && unsafeMethod
            && !websocket
            && context.GetEndpoint()?.Metadata.GetMetadata<Microsoft.AspNetCore.Antiforgery.IAntiforgeryMetadata>() is { RequiresValidation: true })
        {
            try
            {
                await antiforgery.ValidateRequestAsync(context);
            }
            catch (AntiforgeryValidationException)
            {
                invalid = true;
            }
        }

        if (invalid)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await PortalProblems.WriteSanitizedProblemDetails(
                context,
                problems,
                new ProblemDetails
                {
                    Status = 400,
                    Title = "Refresh your secure session",
                    Detail = "Please try again to continue securely.",
                    Extensions = { ["code"] = "csrf_validation_failed" }
                },
                context.RequestAborted);
            return;
        }

        await next(context);
    }
}
