using System.Security.Claims;

namespace DisputePortal.Api.Service.Security;

public static class RateLimitIdentity
{
    public static string UserOrAddress(HttpContext context)
    {
        var id = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        return context.User.Identity?.IsAuthenticated == true && !string.IsNullOrWhiteSpace(id) ? "user:" + id : "ip:" + (context.Connection.RemoteIpAddress?.ToString() ?? "unknown");
    }
}
