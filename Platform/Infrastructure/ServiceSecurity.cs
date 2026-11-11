using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using DisputePortal.Platform.Infrastructure.Models;

namespace DisputePortal.Platform.Infrastructure;

public static class ServiceSecurity
{
    public const string InternalServiceScheme = "PortalInternalService";

    public static IServiceCollection AddServiceSecurity(this IServiceCollection services, ServiceOptions options, string? scheme = null)
    {
        var keys = options.PublicKeys.ToDictionary(
            p => p.Key,
            p =>
        {
            var key = ECDsa.Create();
            key.ImportFromPem(p.Value);
            return (SecurityKey)new ECDsaSecurityKey(key)
            {
                KeyId = p.Key
            };
        });
        var authentication = scheme is null ? services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme) : services.AddAuthentication();
        authentication.AddJwtBearer(
            scheme ?? JwtBearerDefaults.AuthenticationScheme,
            o =>
        {
            o.Events = new JwtBearerEvents
            {
                OnTokenValidated = ctx =>
                {
                    var issuer = ctx.SecurityToken.Issuer;
                    if (ctx.SecurityToken.SigningKey?.KeyId != issuer || ctx.Principal?.FindFirstValue("caller") != issuer)
                    {
                        ctx.Fail("Caller does not match signing-key ownership.");
                    }

                    return Task.CompletedTask;
                }
            };
            o.TokenValidationParameters = new()
            {
                ValidateIssuer = true,
                ValidIssuers = keys.Keys,
                ValidateAudience = true,
                ValidAudience = options.Name,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromSeconds(options.ServiceTokenClockSkewSeconds),
                ValidateIssuerSigningKey = true,
                IssuerSigningKeyResolver = (_, _, kid, _) => keys.TryGetValue(kid, out var key) ? [key] : [],
                ValidAlgorithms = [SecurityAlgorithms.EcdsaSha256]
            };
        });
        services.AddAuthorization();
        return services;
    }

    public static string Token(ServiceOptions options, string audience, ClaimsPrincipal? actor)
    {
        using var key = ECDsa.Create();
        key.ImportFromPem(options.SigningKey);
        var claims = new List<Claim>
        {
            new("caller", options.Name),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };
        if (actor?.Identity?.IsAuthenticated == true)
        {
            var id = actor.FindFirstValue(ClaimTypes.NameIdentifier) ?? actor.FindFirstValue("sub");
            if (id is not null)
            {
                claims.Add(new("sub", id));
            }

            claims.AddRange(actor.FindAll(ClaimTypes.Role).Select(c => new Claim("role", c.Value)));
        }

        var token = new JwtSecurityToken(
            options.Name,
            audience,
            claims,
            DateTime.UtcNow.AddSeconds(-options.ServiceTokenBackdateSeconds),
            DateTime.UtcNow.AddSeconds(options.ServiceTokenLifetimeSeconds),
            new SigningCredentials(
                new ECDsaSecurityKey(key)
                {
                    KeyId = options.Name,
                    CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false }
                },
                SecurityAlgorithms.EcdsaSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public static Guid UserId(this ClaimsPrincipal principal) => Guid.TryParse(
        principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub"),
        out var id)
        && id != Guid.Empty ? id : throw new PlatformException(403, "user_required", "An authenticated user is required.");

    public static void RequireCaller(this ClaimsPrincipal principal, params string[] callers)
    {
        if (!callers.Contains(principal.FindFirstValue("caller")))
        {
            throw new PlatformException(403, "caller_denied", "Service caller is not permitted.");
        }
    }

    public static void RequireAdmin(this ClaimsPrincipal principal)
    {
        if (!principal.IsInRole("super_admin"))
        {
            throw new PlatformException(403, "admin_required", "Administrator permission is required.");
        }
    }
}
