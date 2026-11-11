using DisputePortal.Api.Service.ExternalServices;
using DisputePortal.Api.Models.Security;
using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using DisputePortal.Api.Models;
using DisputePortal.Api.Models.Exceptions;
using DisputePortal.Api.Models.Requests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using DisputePortal.Api.Interfaces.Repositories;
using DisputePortal.Api.Interfaces.Security;

namespace DisputePortal.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class DisputeRulesController(
    IDisputeRiskRepository rules,
    IProfileRepository profiles,
    IAuthenticationRepository repository,
    IIdentityVerificationRepository identities,
    IPasswordHasher<PortalPasswordUser> hasher,
    IVerificationDelivery delivery,
    DisputePortal.Api.Repository.PolicyVersionRepository publications) : PortalControllerBase
{
    [HttpGet("admin/dispute-rules")]
    [Authorize(Roles = "super_admin")]
    public async Task<IActionResult> GetRules(CancellationToken ct) =>
        Ok((await publications.RoutingHistory(UserId(User), 1, 1, null, ct)).Effective.Selected ?? throw new PortalConflictException("No dispute-routing policy applies at this time."));

    [HttpPost("admin/dispute-rules")]
    [Authorize(Roles = "super_admin")]
    [EnableRateLimiting("security")]
    public async Task<IActionResult> UpdateDisputeRiskPolicy(RiskPolicyUpdateRequest request, CancellationToken ct)
    {
        await rules.UpdateRiskPolicy(UserId(User), request, ct);
        return NoContent();
    }

    [HttpGet("admin/dispute-rules/defaults")]
    [Authorize(Roles = "super_admin")]
    public async Task<IActionResult> Defaults(CancellationToken ct) => Ok(await rules.GetPolicyDefaults(ct));

    [HttpPost("admin/dispute-rules/reset")]
    [Authorize(Roles = "super_admin")]
    [EnableRateLimiting("security")]
    public async Task<IActionResult> Reset(ResetDisputePoliciesRequest request, CancellationToken ct) => Ok(await rules.ResetPolicies(
        UserId(User),
        request,
        ct));

    [HttpGet("transactions/{id:guid}/dispute-check")]
    [Authorize(Policy = "customer")]
    public async Task<IActionResult> AssessTransactionDisputeRisk(Guid id, CancellationToken ct) => Ok(await rules.AssessDispute(
        UserId(User),
        id,
        ct));

    private Guid Session() =>
        Guid.TryParse(User.FindFirstValue("session_id"), out var id) ? id : throw new PortalForbiddenException("Sign in again before verifying this transaction.");

    [HttpPost("transactions/{id:guid}/verification/start")]
    [Authorize(Policy = "customer")]
    [EnableRateLimiting("security")]
    public async Task<IActionResult> StartDisputeVerification(Guid id, RiskVerificationStartRequest request, CancellationToken ct)
    {
        var profile = await profiles.GetProfile(UserId(User), ct);
        var contact = string.IsNullOrWhiteSpace(profile.Email) ? profile.Phone ?? "" : profile.Email;
        var login = await repository.FindLogin(contact.ToUpperInvariant(), ct);
        if (login == null
            || login.UserId != UserId(User)
            || hasher.VerifyHashedPassword(new PortalPasswordUser(login.UserId), login.PasswordHash, request.CurrentPassword) == PasswordVerificationResult.Failed)
        {
            return ValidationFailure("password", "Check your current password.");
        }

        var identity = await identities.FindIdentity(contact, ct) ?? throw new PortalForbiddenException();
        var contactMethod = identity.VerifiedContact() ?? throw new PortalValidationException("Verify your email or mobile number in Profile before continuing.");
        var (channel, destination) = contactMethod;
        if (!delivery.IsAvailable(channel))
        {
            throw new VerificationUnavailableException();
        }

        var code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString(CultureInfo.InvariantCulture);
        var challenge = await rules.ReserveDisputeVerification(UserId(User), Session(), id, channel, destination, code, ct);
        await delivery.SendVerificationCode(
            channel,
            destination,
            code,
            VerificationMessage.GetRemainingCodeMinutes(challenge.ExpiresAt),
            VerificationPurpose.DisputeSecurity,
            ct);
        await rules.ReadyDisputeVerification(UserId(User), challenge.ChallengeId, ct);
        return Ok(challenge);
    }

    [HttpPost("transactions/{id:guid}/verification/complete")]
    [Authorize(Policy = "customer")]
    [EnableRateLimiting("security")]
    public async Task<IActionResult> CompleteDisputeVerification(Guid id, RiskVerificationCompleteRequest request, CancellationToken ct)
    {
        await rules.CompleteDisputeVerification(UserId(User), Session(), id, request.ChallengeId, request.Code, ct);
        return NoContent();
    }
}
