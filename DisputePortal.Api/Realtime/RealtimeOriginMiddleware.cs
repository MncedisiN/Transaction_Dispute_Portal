namespace DisputePortal.Api.Realtime;

public class RealtimeOriginMiddleware(RequestDelegate next, IConfiguration configuration)
{
    private readonly string[] origins = configuration.GetSection("AllowedOrigins").Get<string[]>() ?? [];

    public async Task Invoke(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments(PortalRealtimeHub.EndpointPath))
        {
            var origin = context.Request.Headers.Origin;
            var websocket = context.WebSockets.IsWebSocketRequest;
            if ((websocket || origin.Count > 0) && (origin.Count != 1 || !IsAllowedOrigin(origin[0], origins)))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
        }

        await next(context);
    }

    public static bool IsAllowedOrigin(string? origin, IEnumerable<string> allowed) => origin is not null
        && Uri.TryCreate(origin, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https"
        && uri.AbsolutePath == "/"
        && string.IsNullOrEmpty(uri.Query)
        && string.IsNullOrEmpty(uri.Fragment)
        && string.IsNullOrEmpty(uri.UserInfo)
        && allowed.Any(value => string.Equals(value.TrimEnd('/'), uri.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase));
}
