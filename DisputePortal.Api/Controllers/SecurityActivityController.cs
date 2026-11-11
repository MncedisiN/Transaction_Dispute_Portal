using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DisputePortal.Api.Interfaces.Repositories;

namespace DisputePortal.Api.Controllers;

[ApiController]
[Route("api/v1/security")]
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class SecurityActivityController(ISecurityActivityRepository security) : PortalControllerBase
{
    [HttpGet("sign-ins/{id:guid}")]
    public async Task<IActionResult> SignInDetail(Guid id, CancellationToken ct)
    {
        var item = await security.GetSignInDetail(UserId(User), id, ct);
        if (item is null)
        {
            return NotFound();
        }

        item.IsCurrentSession = item.SessionId?.ToString() == User.FindFirstValue("session_id");
        return Ok(item);
    }

    [HttpGet("sign-ins")]
    public async Task<IActionResult> SignIns(CancellationToken ct) => Ok(await security.GetRecentSignIns(UserId(User), ct));

    [HttpGet("activity")]
    public async Task<IActionResult> Activity([FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct)
    {
        var paging = Page(page, pageSize);
        return Ok(await security.GetSecurityActivity(UserId(User), paging.Page, paging.PageSize, ct));
    }

    [HttpGet("audit")]
    [Authorize(Policy = "audit.read")]
    public async Task<IActionResult> Audit([FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct)
    {
        var paging = Page(page, pageSize);
        return Ok(await security.GetSecurityActivity(null, paging.Page, paging.PageSize, ct));
    }

    [HttpGet("sessions")]
    public async Task<IActionResult> Sessions(CancellationToken ct) => Ok(new
    {
        currentSession = User.FindFirstValue("session_id"),
        sessions = await security.GetActiveSessions(UserId(User), ct)
    });

    [HttpPost("sessions/{id:guid}/revoke")]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken ct)
    {
        var user = UserId(User);
        await security.RevokeOwnedSession(user, id, ct);
        await security.RecordSecurityEvent(
            user,
            "SessionRevoked",
            "Success",
            HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unavailable",
            Request.Headers.UserAgent.ToString(),
            null,
            ct);
        if (User.FindFirstValue("session_id") == id.ToString())
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }

        return NoContent();
    }
}
