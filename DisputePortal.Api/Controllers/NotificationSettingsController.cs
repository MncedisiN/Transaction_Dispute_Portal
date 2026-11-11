using System.Security.Claims;
using System.Security.Cryptography;
using DisputePortal.Api.Configuration;
using DisputePortal.Api.Models.Requests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using DisputePortal.Api.Interfaces.Repositories;
using DisputePortal.Api.Interfaces.Security;

namespace DisputePortal.Api.Controllers;

[ApiController]
[Route("api/v1/notification-settings")]
[Authorize]
[EnableRateLimiting("sensitive")]
public class NotificationSettingsController(
    INotificationDeliveryRepository repository,
    PushOptions push,
    IPushEndpointPolicy endpointPolicy) : PortalControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetNotificationPreferences(CancellationToken ct)
    {
        var user = UserId(User);
        var available = push.IsConfigured;
        var subscribed = available
            && Guid.TryParse(User.FindFirstValue("session_id"), out var session)
            && (await repository.GetPushSubscriptions(user, ct, session)).Count > 0;
        return Ok(new
        {
            channel = await repository.GetNotificationPreference(user, ct),
            pushAvailable = available,
            pushSubscribed = subscribed,
            publicKey = available ? push.PublicKey : null
        });
    }

    [HttpPost]
    public async Task<IActionResult> SaveNotificationPreferences(NotificationPreferenceRequest request, CancellationToken ct)
    {
        await repository.SaveNotificationPreference(UserId(User), request.Channel, ct);
        return NoContent();
    }

    [HttpPost("push")]
    public async Task<IActionResult> Subscribe(PushSubscriptionRequest request, CancellationToken ct)
    {
        if (!push.IsConfigured)
        {
            return ValidationFailure("push", "Push delivery is being configured.");
        }

        if (!endpointPolicy.IsAllowed(request.Endpoint))
        {
            return ValidationFailure("endpoint", "This push service is not supported.");
        }

        try
        {
            var key = PushOptions.DecodeBase64Url(request.P256dh);
            if (key.Length != 65 || key[0] != 4 || PushOptions.DecodeBase64Url(request.Auth).Length != 16)
            {
                return ValidationFailure("keys", "Use a valid browser push subscription.");
            }

            using var agreement = ECDiffieHellman.Create(new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint
                {
                    X = key[1..33],
                    Y = key[33..65]
                }
            });
        }
        catch (Exception error) when (error is FormatException or CryptographicException or ArgumentException)
        {
            return ValidationFailure("keys", "Use a valid browser push subscription.");
        }

        if (!Guid.TryParse(User.FindFirstValue("session_id"), out var session))
        {
            return Forbid();
        }

        await repository.SavePushSubscription(UserId(User), session, request, ct);
        return NoContent();
    }

    [HttpPost("push/disable")]
    public async Task<IActionResult> Disable(CancellationToken ct)
    {
        if (Guid.TryParse(User.FindFirstValue("session_id"), out var session))
        {
            await repository.RemovePushSubscriptions(UserId(User), session, ct);
        }

        return NoContent();
    }
}
