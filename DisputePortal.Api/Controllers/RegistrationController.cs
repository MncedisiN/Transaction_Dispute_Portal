using DisputePortal.Api.Service.ExternalServices;
using DisputePortal.Api.Models.Security;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using DisputePortal.Api.Configuration;
using DisputePortal.Api.Models;
using DisputePortal.Api.Models.Domain;
using DisputePortal.Api.Models.Exceptions;
using DisputePortal.Api.Models.Internal;
using DisputePortal.Api.Models.Requests;
using DisputePortal.Api.Models.Responses;
using DisputePortal.Api.Service.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using DisputePortal.Api.Interfaces.Security;
using DisputePortal.Api.Interfaces.Repositories;

namespace DisputePortal.Api.Controllers;

[ApiController]
[Route("api/v1/identity")]
[AllowAnonymous]
[EnableRateLimiting("auth")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class RegistrationController(
    IIdentityVerificationRepository identity,
    IVerificationDelivery delivery,
    IPasswordHasher<PortalPasswordUser> hasher,
    IConfiguration configuration,
    ILogger<RegistrationController> logger,
    PasswordSafety? safety = null,
    IRegistrationProfileValidator? profiles = null,
    IRegistrationSessionIssuer? sessions = null,
    IHostEnvironment? environment = null) : PortalControllerBase
{
    private bool SkipContactVerification => AssessmentSecurity.AllowsUnverifiedContacts(configuration, environment);

    private bool UsesExternalIdentity => AuthenticationModes.UsesBearerTokens(configuration["Authentication:Mode"]);

    public static bool IsStrongPassword(string password) => PasswordPolicy.IsStrong(password);

    private static bool ValidContact(string contact, string channel) =>
        channel == "email" ? new EmailAddressAttribute().IsValid(contact) : Regex.IsMatch(contact, @"^\+[1-9]\d{7,14}$");

    [HttpGet("channels")]
    public IActionResult Channels() => Ok(new
    {
        channels = UsesExternalIdentity ? Array.Empty<string>() : SkipContactVerification ? new[] { "email", "sms" } : new[] { "email", "sms", "whatsapp" }.Where(delivery.IsAvailable).ToArray(),
        verificationRequired = !SkipContactVerification
    });

    [HttpPost("register")]
    public async Task<IActionResult> RegisterCustomerAccount(RegisterRequest request, CancellationToken ct)
    {
        if (UsesExternalIdentity)
        {
            return NotFound();
        }

        if ((request.FirstName.Trim() + " " + request.LastName.Trim()).Length > 200)
        {
            return ValidationFailure("firstName", "Keep your combined name within 200 characters.");
        }

        var contact = request.Contact.Trim();
        if (request.Channel != "email" && ContactNumber.TryNormalize(contact, out var phone))
        {
            contact = phone;
        }

        if (!ValidContact(contact, request.Channel))
        {
            return ValidationFailure("contact", "Enter a valid email or mobile number with country code.");
        }

        if (!IsStrongPassword(request.Password))
        {
            return ValidationFailure("password", "Use at least 12 characters with upper and lower case letters, a number and a symbol.");
        }

        if (await identity.RegistrationContactExists(contact, ct))
        {
            throw new PortalConflictException("This contact is already registered. Sign in or use password recovery.");
        }

        if (safety is not null)
        {
            await safety.ValidatePasswordStrength(request.Password, ct);
        }

        if (!SkipContactVerification && !delivery.IsAvailable(request.Channel))
        {
            throw new VerificationUnavailableException();
        }

        var details = profiles is null ? null : await profiles.ValidateRegistrationProfile(request.Details, contact, request.Channel, ct);
        if (details is not null
            && (await identity.RegistrationContactExists(details.Email, ct)
            || await identity.RegistrationContactExists(details.Phone, ct)))
        {
            throw new PortalConflictException("This email or mobile number is already registered. Sign in or use password recovery.");
        }

        var payload = new RegistrationPayload(
            request.FirstName.Trim() + " " + request.LastName.Trim(),
            contact,
            request.Channel,
            hasher.HashPassword(new PortalPasswordUser(Guid.Empty), request.Password),
            request.FirstName.Trim(),
            request.LastName.Trim(),
            details);
        if (SkipContactVerification)
        {
            var id = await identity.RegisterWithoutContactVerification(payload, ct);
            if (sessions is not null)
            {
                await sessions.Issue(id, HttpContext, ct);
            }
            return Ok(new { authenticated = sessions is not null, requiresVerification = false });
        }

        var challenge = await StartIdentityVerificationChallenge(
            null,
            "Registration",
            request.Channel,
            contact,
            JsonSerializer.Serialize(payload),
            ct);
        return Ok(challenge);
    }

    [HttpPost("register/verify")]
    public async Task<IActionResult> VerifyRegistration(VerifyIdentityRequest request, CancellationToken ct)
    {
        if (UsesExternalIdentity)
        {
            return NotFound();
        }

        var id = await identity.CompleteRegistration(request.ChallengeId, request.Code, ct);
        if (sessions is not null)
        {
            await sessions.Issue(id, HttpContext, ct);
            return Ok(new { message = "Your account is verified.", authenticated = true });
        }

        return Ok(new { message = "Your account is verified. You can now sign in." });
    }

    [HttpPost("password/forgot")]
    public async Task<IActionResult> Forgot(RecoveryRequest request, CancellationToken ct)
    {
        if (UsesExternalIdentity)
        {
            return NotFound();
        }

        var contact = request.Contact.Trim();
        if (ContactNumber.TryNormalize(contact, out var phone))
        {
            contact = phone;
        }

        if (!ValidContact(contact, request.Channel) && !Regex.IsMatch(contact, @"^[A-Za-z][A-Za-z0-9._-]{2,49}$"))
        {
            return ValidationFailure("contact", "Enter your registered email, mobile number or username.");
        }

        if (!delivery.IsAvailable(request.Channel))
        {
            throw new VerificationUnavailableException();
        }

        var user = await identity.FindIdentity(contact, ct);
        var destination = request.Channel == "email" ? (user?.EmailVerified == true ? user.Email : null) : (user?.PhoneVerified == true ? user.Phone : null);
        var ownsChannel = destination is not null;

        var code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString(CultureInfo.InvariantCulture);
        var challenge = await identity.CreateIdentityChallenge(
            ownsChannel ? user!.Id : null,
            "Recovery",
            request.Channel,
            destination ?? contact,
            "",
            code,
            ct);
        if (ownsChannel)
        {
            try
            {
                await delivery.SendVerificationCode(
                    request.Channel,
                    destination!,
                    code,
                    VerificationMessage.GetRemainingCodeMinutes(challenge.ExpiresAt),
                    VerificationPurpose.PasswordReset,
                    ct);
                await identity.MarkIdentityChallengeReady(challenge.ChallengeId, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                logger.LogWarning("Recovery delivery unavailable for channel {Channel}", request.Channel);
            }
        }

        return Ok(new IdentityChallengeDto(challenge) { MaskedDestination = "your registered contact" });
    }

    [HttpPost("password/reset")]
    public async Task<IActionResult> Reset(ResetPasswordRequest request, CancellationToken ct)
    {
        if (UsesExternalIdentity)
        {
            return NotFound();
        }

        if (!IsStrongPassword(request.Password))
        {
            return ValidationFailure("password", "Use at least 12 characters with upper and lower case letters, a number and a symbol.");
        }

        if (safety is not null)
        {
            await safety.ValidatePasswordStrength(request.Password, ct);
        }

        await identity.CompletePasswordReset(
            request.ChallengeId,
            request.Code,
            hasher.HashPassword(new PortalPasswordUser(Guid.Empty), request.Password),
            ct,
            hash => hasher.VerifyHashedPassword(new PortalPasswordUser(Guid.Empty), hash, request.Password) != PasswordVerificationResult.Failed);
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return NoContent();
    }

    private async Task<IdentityChallengeDto> StartIdentityVerificationChallenge(
        Guid? user,
        string purpose,
        string channel,
        string destination,
        string payload,
        CancellationToken ct)
    {
        var code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString(CultureInfo.InvariantCulture);
        var challenge = await identity.CreateIdentityChallenge(user, purpose, channel, destination, payload, code, ct);
        await delivery.SendVerificationCode(
            channel,
            destination,
            code,
            VerificationMessage.GetRemainingCodeMinutes(challenge.ExpiresAt),
            VerificationPurpose.Registration,
            ct);
        await identity.MarkIdentityChallengeReady(challenge.ChallengeId, ct);
        return challenge;
    }
}
