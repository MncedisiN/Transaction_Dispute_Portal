using DisputePortal.Api.Configuration;
using DisputePortal.Api.Models;
using DisputePortal.Api.Models.Requests;
using DisputePortal.Api.Service.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using DisputePortal.Api.Interfaces.Repositories;

namespace DisputePortal.Api.Controllers;

[ApiController]
[Route("api/v1/profile/password")]
[Authorize]
[EnableRateLimiting("security")]
public class PasswordController(
    IPasswordChangeRepository passwords,
    IPasswordHasher<PortalPasswordUser> hasher,
    PasswordSafety safety,
    IConfiguration configuration) : PortalControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Change(ChangePasswordRequest request, CancellationToken ct)
    {
        if (AuthenticationModes.UsesBearerTokens(configuration["Authentication:Mode"]))
        {
            return NotFound();
        }

        if (request.NewPassword != request.ConfirmPassword)
        {
            return ValidationFailure("confirmPassword", "Passwords must match.");
        }

        var user = UserId(User);
        var state = await passwords.GetPasswordState(user, ct);
        if (state is null
            || state.Status != "Active"
            || hasher.VerifyHashedPassword(new(user), state.PasswordHash, request.CurrentPassword) == PasswordVerificationResult.Failed)
        {
            return ValidationFailure("currentPassword", "Check your current password.");
        }

        if (hasher.VerifyHashedPassword(new(user), state.PasswordHash, request.NewPassword) != PasswordVerificationResult.Failed)
        {
            return ValidationFailure("newPassword", "Choose a different password from your current password.");
        }

        await safety.ValidatePasswordStrength(request.NewPassword, ct);
        await passwords.ChangePassword(user, state.PasswordHash, hasher.HashPassword(new(user), request.NewPassword), ct);
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return NoContent();
    }
}
