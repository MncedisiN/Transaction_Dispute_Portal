using System.Security.Claims;
using DisputePortal.Api.Models.Requests;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using DisputePortal.Api.Interfaces.Repositories;
using DisputePortal.Api.Interfaces.Security;

namespace DisputePortal.Api.Controllers;

[ApiController]
[Route("api/v1/auth/login-verification")]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class LoginVerificationSettingsController(
    ILoginSecurityRepository loginSecurity,
    IPasswordVerifier passwords,
    IAuthenticationRepository authentication,
    IProfileRepository profiles) : PortalControllerBase
{
    [HttpPost]
    [EnableRateLimiting("security")]
    public async Task<IActionResult> UpdateLoginVerificationPolicy(LoginVerificationSettingsRequest request, CancellationToken ct)
    {
        if (User.Identity?.IsAuthenticated != true
            || User.Identity.AuthenticationType != CookieAuthenticationDefaults.AuthenticationScheme
            || !Guid.TryParse(User.FindFirstValue("session_id"), out var session)
            || !await authentication.IsSessionActive(session, DateTimeOffset.UtcNow, ct))
        {
            return Unauthorized();
        }

        if (request.Enabled is not { } enabled)
        {
            return ValidationFailure("enabled", "Choose whether to use two-step verification.");
        }

        var user = UserId(User);
        var state = await authentication.GetPasswordState(user, ct);
        if (state is null
            || state.Status != "Active"
            || string.IsNullOrEmpty(request.CurrentPassword)
            || !passwords.Verify(user, state.PasswordHash, request.CurrentPassword))
        {
            return ValidationFailure("currentPassword", "Check your current password.");
        }

        var settings = await loginSecurity.GetDevices(user, null, ct);
        if (!settings.CanChangeVerification)
        {
            return ValidationFailure(
                "enabled",
                settings.VerificationPolicyMessage ?? "Your sign-in verification is managed by your administrator.");
        }

        if (enabled)
        {
            var profile = await profiles.GetProfile(user, ct);
            if (string.IsNullOrWhiteSpace(profile.Email) && string.IsNullOrWhiteSpace(profile.Phone))
            {
                return ValidationFailure("enabled", "Add your email or mobile number before enabling two-step verification.");
            }
        }

        await loginSecurity.SetLoginVerification(user, state.PasswordHash, enabled, ct);
        Response.Cookies.Delete(
            "resolve.trusted-device",
            new CookieOptions
            {
                Path = "/api/v1/auth",
                HttpOnly = true,
                SameSite = SameSiteMode.Strict,
                Secure = Request.IsHttps,
                IsEssential = true
            });
        return Ok(await loginSecurity.GetDevices(user, null, ct));
    }
}
