using System.Security.Claims;
using DisputePortal.Api.Configuration;
using DisputePortal.Api.Models.Domain;
using DisputePortal.Api.Models.Exceptions;
using DisputePortal.Api.Models.Requests;
using DisputePortal.Api.Service.Security;
using FluentValidation;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using DisputePortal.Api.Interfaces.Repositories;

namespace DisputePortal.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
[Tags("Authentication")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class AuthController(
    IAntiforgery antiforgery,
    IConfiguration configuration,
    IAuthenticationRepository repository,
    ISecurityActivityRepository security,
    IValidator<SignInRequest> signInValidator,
    SessionPolicy sessionPolicy,
    ILoginSecurityRepository loginSecurity,
    SignInService signIn) : PortalControllerBase
{
    private const string FlowCookie = "resolve.login-flow";
    private const string DeviceCookie = "resolve.trusted-device";

    [HttpGet("csrf")]
    [AllowAnonymous]
    public IActionResult GetCsrfToken()
    {
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        return Ok(new { requestToken = tokens.RequestToken });
    }

    [HttpPost("sign-in")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> SignIn([FromBody] SignInRequest request, CancellationToken cancellationToken)
    {
        if (AuthenticationModes.UsesBearerTokens(configuration["Authentication:Mode"]))
        {
            return NotFound();
        }

        var validation = await signInValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return ValidationFailure(validation);
        }

        var result = await signIn.StartSignIn(
            request.LoginIdentifier,
            request.Password,
            Request.Cookies[DeviceCookie],
            Request.Headers.UserAgent.ToString(),
            cancellationToken);
        if (!result.PasswordAccepted)
        {
            await Audit(result.UserId, "PasswordCheck", "Rejected", null, cancellationToken);
            return Problem(statusCode: 401, title: "Sign-in failed", detail: "Check your sign-in details and try again.");
        }

        if (result.Session is not null)
        {
            return await CompleteSession(result.Session, cancellationToken);
        }

        if (result.Challenge is null)
        {
            return Problem(
                statusCode: 403,
                title: "Verification required",
                detail: "Ask your administrator to restore your registered contact details.");
        }

        Response.Cookies.Append(FlowCookie, result.FlowCookie!, CookieOptions(result.Challenge.FlowExpiresAt));
        await Audit(result.UserId, "OtpChallenge", "Pending", null, cancellationToken);
        return Ok(result.Challenge);
    }

    [HttpPost("sign-in/resend")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> ResendSignIn(ResendSignInRequest request, CancellationToken cancellationToken)
    {
        if (AuthenticationModes.UsesBearerTokens(configuration["Authentication:Mode"]))
        {
            return NotFound();
        }

        var challenge = await signIn.ResendCode(
            request.ChallengeId,
            FlowSecret(request.ChallengeId),
            Request.Headers.UserAgent.ToString(),
            cancellationToken);
        return Ok(challenge);
    }

    [HttpPost("sign-in/verify")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> VerifySignIn(VerifySignInRequest request, CancellationToken cancellationToken)
    {
        if (AuthenticationModes.UsesBearerTokens(configuration["Authentication:Mode"]))
        {
            return NotFound();
        }

        LoginSessionGrant grant;
        try
        {
            grant = await loginSecurity.CompleteOtp(
                request.ChallengeId,
                FlowSecret(request.ChallengeId),
                Request.Headers.UserAgent.ToString(),
                request.Code,
                request.RememberDevice,
                cancellationToken);
        }
        catch (PortalValidationException)
        {
            await Audit(null, "OtpCheck", "Rejected", null, cancellationToken);
            throw;
        }

        Response.Cookies.Delete(FlowCookie, CookieOptions());
        return await CompleteSession(grant, cancellationToken);
    }

    [HttpGet("devices")]
    [Authorize]
    public async Task<IActionResult> Devices(CancellationToken cancellationToken) => Ok(await loginSecurity.GetDevices(
        UserId(User),
        Request.Cookies[DeviceCookie],
        cancellationToken));

    [HttpPost("devices/{id:guid}/revoke")]
    [Authorize]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> RevokeDevice(Guid id, CancellationToken cancellationToken)
    {
        await loginSecurity.RevokeDevice(UserId(User), id, cancellationToken);
        if (LoginDeviceToken.TryParse(Request.Cookies[DeviceCookie], out var current, out _) && current == id)
        {
            Response.Cookies.Delete(DeviceCookie, CookieOptions());
        }

        return NoContent();
    }

    [HttpPost("devices/revoke-all")]
    [Authorize]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> RevokeDevices(CancellationToken cancellationToken)
    {
        await loginSecurity.RevokeDevice(UserId(User), null, cancellationToken);
        Response.Cookies.Delete(DeviceCookie, CookieOptions());
        return NoContent();
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> Me(CancellationToken cancellationToken)
    {
        var user = await repository.GetCurrentUser(UserId(User), cancellationToken);
        return user is null ? Unauthorized() : Ok(user);
    }

    [HttpPost("sign-out")]
    [Authorize]
    public async Task<IActionResult> SignOutCurrentSession(CancellationToken cancellationToken)
    {
        if (Guid.TryParse(User.FindFirstValue("session_id"), out var sessionId))
        {
            await repository.RevokeSession(sessionId, DateTimeOffset.UtcNow, cancellationToken);
        }

        await Audit(UserId(User), "SignOut", "Success", null, cancellationToken);
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return NoContent();
    }

    private async Task<IActionResult> CompleteSession(LoginSessionGrant grant, CancellationToken cancellationToken)
    {
        var access = await signIn.GetSessionAccess(grant, cancellationToken);
        if (access.VerificationRequired)
        {
            return Problem(statusCode: 403, title: "Verification required", detail: "Sign in again to verify your access.");
        }

        var user = access.User;
        if (user is null)
        {
            return Unauthorized();
        }

        var expires = DateTimeOffset.UtcNow.AddMinutes(sessionPolicy.AbsoluteMinutes);
        if (Guid.TryParse(User.FindFirstValue("session_id"), out var previousSession))
        {
            await repository.RevokeSession(previousSession, DateTimeOffset.UtcNow, cancellationToken);
        }

        await Audit(user.Id, "SignIn", "Success", grant.SessionId, cancellationToken);
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.DisplayName),
            new("session_id", grant.SessionId.ToString())
        };
        claims.AddRange(user.Roles.Select(role => new Claim(ClaimTypes.Role, role)));
        claims.AddRange(user.Permissions.Select(permission => new Claim("permission", permission)));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = false,
                AllowRefresh = false,
                ExpiresUtc = expires
            });
        if (grant.DeviceCookie is not null && access.CanRememberDevice)
        {
            Response.Cookies.Append(DeviceCookie, grant.DeviceCookie, CookieOptions(grant.DeviceExpiresAt));
        }

        return Ok(new
        {
            user = new
            {
                id = user.Id,
                displayName = user.DisplayName,
                roles = user.Roles,
                permissions = user.Permissions
            }
        });
    }

    private CookieOptions CookieOptions(DateTimeOffset? expires = null) => new()
    {
        HttpOnly = true,
        Secure = Request.IsHttps || HttpContext.RequestServices.GetService<IHostEnvironment>()?.IsDevelopment() != true,
        SameSite = SameSiteMode.Strict,
        Path = "/api/v1/auth",
        Expires = expires,
        IsEssential = true
    };

    private string FlowSecret(Guid challenge)
    {
        if (!LoginDeviceToken.TryParse(Request.Cookies[FlowCookie], out var id, out var secret) || id != challenge)
        {
            throw new PortalValidationException("This verification is unavailable. Sign in again.");
        }

        return secret;
    }

    private Task Audit(Guid? user, string eventType, string outcome, Guid? session, CancellationToken cancellationToken) =>
        security.RecordSecurityEvent(
        user,
        eventType,
        outcome,
        HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unavailable",
        Request.Headers.UserAgent.ToString(),
        session,
        cancellationToken);
}
