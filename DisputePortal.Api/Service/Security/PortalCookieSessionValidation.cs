using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using DisputePortal.Api.Interfaces.Repositories;
using DisputePortal.Api.Interfaces.Security;

namespace DisputePortal.Api.Service.Security;

public static class PortalCookieSessionValidation
{
    public static async Task ValidateCookieSession(CookieValidatePrincipalContext context)
    {
        if (!Guid.TryParse(context.Principal?.FindFirstValue("session_id"), out var session)
            || !Guid.TryParse(context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var user))
        {
            context.RejectPrincipal();
            return;
        }

        var services = context.HttpContext.RequestServices;
        var ct = context.HttpContext.RequestAborted;
        var now = DateTimeOffset.UtcNow;
        var sessions = services.GetRequiredService<IRealtimeSessionReader>();
        if (!await sessions.IsActive(user, session, now, ct))
        {
            context.RejectPrincipal();
            return;
        }

        var authentication = services.GetRequiredService<IAuthenticationRepository>();
        var current = await authentication.GetCurrentUser(user, ct);
        if (current is null)
        {
            context.RejectPrincipal();
            return;
        }

        var identity = (context.Principal!.Identity as ClaimsIdentity)?.Clone();
        if (identity is null)
        {
            context.RejectPrincipal();
            return;
        }

        var previous = identity.Claims.Where(claim => claim.Type == ClaimTypes.Role || claim.Type == "permission").Select(claim => claim.Type + ":" + claim.Value).Order(StringComparer.Ordinal).ToArray();
        var expected = current.Roles.Select(role => new Claim(ClaimTypes.Role, role)).Concat(current.Permissions.Select(permission => new Claim(
            "permission",
            permission))).ToArray();
        if (!previous.SequenceEqual(expected.Select(claim => claim.Type + ":" + claim.Value).Order(StringComparer.Ordinal)))
        {
            foreach (var claim in identity.Claims.Where(claim => claim.Type == ClaimTypes.Role || claim.Type == "permission").ToArray())
                identity.RemoveClaim(claim);
            identity.AddClaims(expected);
            context.ReplacePrincipal(new ClaimsPrincipal(identity));
            context.ShouldRenew = true;
        }

        if (ShouldRecordActivity(context.Request))
        {
            if (!await authentication.IsSessionActive(session, now, ct))
            {
                context.RejectPrincipal();
            }
        }
    }

    public static bool ShouldRecordActivity(HttpRequest request) => request.Path.StartsWithSegments("/api/v1")
        && !request.Path.StartsWithSegments("/api/v1/realtime")
        && request.Path != "/api/v1/auth/csrf"
        && request.Path != "/api/v1/auth/me"
        && request.Headers.TryGetValue("X-Portal-Activity", out var activity)
        && activity.Count == 1
        && activity[0] == "1";
}
