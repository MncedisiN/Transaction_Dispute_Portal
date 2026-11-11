using System.Security.Claims;
using DisputePortal.Api.Models.Domain;
using DisputePortal.Api.Models.Exceptions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using DisputePortal.Api.Interfaces.Security;
using DisputePortal.Api.Interfaces.Repositories;

namespace DisputePortal.Api.Platform;

public class RegistrationSessionIssuer(IAuthenticationRepository users, SessionPolicy policy) : IRegistrationSessionIssuer
{
    public async Task Issue(Guid id, HttpContext context, CancellationToken ct)
    {
        var user = await users.GetCurrentUser(id, ct) ?? throw new PortalNotFoundException();
        if (!user.Roles.Contains("customer") || user.Roles.Any(r => r != "customer"))
        {
            throw new PortalForbiddenException("Sign in to verify staff access.");
        }

        var expires = DateTimeOffset.UtcNow.AddMinutes(policy.AbsoluteMinutes);
        var session = await users.CreateSession(id, expires, ct);
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, id.ToString()),
            new(ClaimTypes.Name, user.DisplayName),
            new("session_id", session.ToString())
        };
        claims.AddRange(user.Roles.Select(r => new Claim(ClaimTypes.Role, r)));
        claims.AddRange(user.Permissions.Select(p => new Claim("permission", p)));
        await context.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)),
            new AuthenticationProperties
            {
                IsPersistent = false,
                AllowRefresh = false,
                ExpiresUtc = expires
            });
    }
}
