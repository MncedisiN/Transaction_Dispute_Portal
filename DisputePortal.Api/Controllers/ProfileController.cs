using DisputePortal.Api.Service.ExternalServices;
using DisputePortal.Api.Models.Security;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using DisputePortal.Api.Models;
using DisputePortal.Api.Models.Domain;
using DisputePortal.Api.Models.Exceptions;
using DisputePortal.Api.Models.Requests;
using DisputePortal.Api.Platform;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using DisputePortal.Api.Interfaces.Repositories;
using DisputePortal.Api.Interfaces.Security;

namespace DisputePortal.Api.Controllers;

[ApiController]
[Route("api/v1/profile")]
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class ProfileController(
    IProfileRepository profiles,
    IAuthenticationRepository repository,
    IPasswordHasher<PortalPasswordUser> hasher,
    IContactVerificationRepository verification,
    IVerificationDelivery delivery,
    ProfileDocumentService? documents = null) : PortalControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Profile(CancellationToken ct) => Ok(await profiles.GetProfile(UserId(User), ct));

    [HttpGet("identity/number")]
    [EnableRateLimiting("security")]
    public async Task<IActionResult> RevealIdentity(CancellationToken ct) => Ok(new
    {
        identityNumber = await profiles.RevealIdentityNumber(UserId(User), ct)
    });

    [HttpPost]
    [EnableRateLimiting("sensitive")]
    public async Task<IActionResult> UpdateCustomerProfile(ProfileDetailsRequest request, CancellationToken ct)
    {
        await profiles.UpdateProfileDetails(UserId(User), request, ct);
        return NoContent();
    }

    [HttpPost("identity")]
    [EnableRateLimiting("security")]
    public async Task<IActionResult> SetIdentity(SetIdentityRequest request, CancellationToken ct)
    {
        if (!await CheckPassword(request.CurrentPassword, ct))
        {
            return ValidationFailure("password", "Check your current password.");
        }

        await profiles.SetIdentity(UserId(User), request.IdentityNumber, request.BirthCentury, ct);
        return NoContent();
    }

    [HttpPost("document")]
    [EnableRateLimiting("security")]
    public async Task<IActionResult> Document(ProfileDocumentRequest request, CancellationToken ct)
    {
        if (!await CheckPassword(request.CurrentPassword, ct))
        {
            return ValidationFailure("password", "Check your current password.");
        }

        var writer = documents ?? throw new PortalConflictException("Document updates are temporarily unavailable.");
        await writer.SaveIdentityDocument(UserId(User), request, ct);
        return NoContent();
    }

    private async Task<bool> CheckPassword(string password, CancellationToken ct)
    {
        var user = UserId(User);
        var state = await repository.GetPasswordState(user, ct);
        return state?.Status == "Active"
            && hasher.VerifyHashedPassword(new PortalPasswordUser(user), state.PasswordHash, password) != PasswordVerificationResult.Failed;
    }

    [HttpGet("verification/channels")]
    public IActionResult Channels() => Ok(new { channels = new[] { "email", "sms", "whatsapp" }.Where(delivery.IsAvailable).ToArray() });

    [HttpPost("verification/start")]
    [EnableRateLimiting("security")]
    public async Task<IActionResult> StartVerification(StartContactVerificationRequest request, CancellationToken ct)
    {
        if (!await CheckPassword(request.CurrentPassword, ct))
        {
            return ValidationFailure("password", "Check your current password.");
        }

        var destination = request.Destination.Trim();
        if (request.Field == "phone")
        {
            if (!ContactNumber.TryNormalize(destination, request.PhoneCountry, out var phone))
            {
                return ValidationFailure("destination", "Check the mobile number and its selected country code.");
            }

            destination = phone;
        }

        if (request.Field == "email")
        {
            if (request.Channel != "email" || !new EmailAddressAttribute().IsValid(destination))
            {
                return ValidationFailure("destination", "Enter a valid email and use email verification.");
            }
        }
        else if (request.Channel is not ("sms" or "whatsapp") || !Regex.IsMatch(destination, @"^\+[1-9]\d{7,14}$"))
        {
            return ValidationFailure("destination", "Enter a phone number with country code, for example +27821234567.");
        }

        if (!delivery.IsAvailable(request.Channel))
        {
            throw new VerificationUnavailableException();
        }

        var code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString(CultureInfo.InvariantCulture);
        var challenge = await verification.ReserveContactChallenge(UserId(User), request.Field, destination, request.Channel, code, ct);
        await delivery.SendVerificationCode(
            request.Channel,
            destination,
            code,
            VerificationMessage.GetRemainingCodeMinutes(challenge.ExpiresAt),
            request.Field == "email" ? VerificationPurpose.ChangeEmail : VerificationPurpose.ChangePhone,
            ct);
        await verification.SetContactChallengeDelivery(UserId(User), challenge.Id, true, ct);
        return Ok(challenge);
    }

    [HttpPost("verification/complete")]
    [EnableRateLimiting("security")]
    public async Task<IActionResult> CompleteVerification(CompleteContactVerificationRequest request, CancellationToken ct)
    {
        if (!await CheckPassword(request.CurrentPassword, ct))
        {
            return ValidationFailure("password", "Check your current password.");
        }

        await verification.CompleteContactChange(UserId(User), request.ChallengeId, request.Code, ct);
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return NoContent();
    }
}
