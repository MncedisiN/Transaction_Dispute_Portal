using DisputePortal.Api.Service.ExternalServices;
using DisputePortal.Api.Models.Security;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Cryptography;
using DisputePortal.Api.Models;
using DisputePortal.Api.Models.Domain;
using DisputePortal.Api.Models.Exceptions;
using DisputePortal.Api.Models.Responses;
using Microsoft.AspNetCore.Identity;
using DisputePortal.Api.Interfaces.Repositories;
using DisputePortal.Api.Interfaces.Security;

namespace DisputePortal.Api.Service.Security;

public class SignInService(
    IAuthenticationRepository repository,
    IIdentityVerificationRepository identity,
    IVerificationDelivery delivery,
    IPasswordHasher<PortalPasswordUser> passwordHasher,
    ILoginSecurityRepository loginSecurity,
    LoginVerificationPolicy loginPolicy)
{
    private const string DummyPasswordHash = "AQAAAAIAAYagAAAAEFHG+QA2/dAjEXyT6AITbccQjP63dRzEzB9/p4oPMqQXnkfWNBoBV7LRO3EZwDA7Fw==";

    public async Task<PasswordSignInResult> StartSignIn(
        string loginIdentifier,
        string password,
        string? deviceCookie,
        string userAgent,
        CancellationToken cancellationToken)
    {
        var identifier = loginIdentifier.Trim();
        if (ContactNumber.TryNormalize(identifier, out var phone))
        {
            identifier = phone;
        }

        var user = await repository.FindLogin(identifier.ToUpperInvariant(), cancellationToken);
        var candidate = new PortalPasswordUser(user?.UserId ?? Guid.Empty);
        var verification = passwordHasher.VerifyHashedPassword(candidate, user?.PasswordHash ?? DummyPasswordHash, password);
        if (user is null || user.AccountStatus != "Active" || verification == PasswordVerificationResult.Failed)
        {
            return new PasswordSignInResult
            {
                UserId = user?.UserId
            };
        }

        // A remembered device is considered only after the password has been verified.
        var grant = await loginSecurity.TryPasswordSession(user.UserId, user.PasswordHash, deviceCookie, userAgent, cancellationToken);
        if (grant is not null)
        {
            return new PasswordSignInResult
            {
                PasswordAccepted = true,
                UserId = user.UserId,
                Session = grant
            };
        }

        var verifiedIdentity = await identity.FindIdentity(user.LoginIdentifier, cancellationToken);
        var contact = verifiedIdentity?.VerifiedContact();
        var verifyRegisteredContact = contact is null;
        if (contact is null)
        {
            // First-use verification may target only the identifier already registered to this account.
            if (new EmailAddressAttribute().IsValid(user.LoginIdentifier))
            {
                contact = ("email", user.LoginIdentifier);
            }
            else if (ContactNumber.TryNormalize(user.LoginIdentifier, out var registeredPhone))
            {
                contact = ("sms", registeredPhone);
            }
            else
            {
                return new PasswordSignInResult
                {
                    PasswordAccepted = true,
                    UserId = user.UserId
                };
            }
        }

        var (channel, destination) = contact.Value;
        if (!delivery.IsAvailable(channel))
        {
            throw new VerificationUnavailableException();
        }

        var code = NewCode();
        var challenge = await identity.CreateIdentityChallenge(user.UserId, "Login", channel, destination, "", code, cancellationToken);
        var secret = LoginDeviceToken.NewSecret();
        var flow = await loginSecurity.CreateFlow(
            user.UserId,
            challenge.ChallengeId,
            user.PasswordHash,
            secret,
            userAgent,
            destination,
            verifyRegisteredContact,
            cancellationToken);
        await delivery.SendVerificationCode(
            channel,
            destination,
            code,
            VerificationMessage.GetRemainingCodeMinutes(challenge.ExpiresAt),
            VerificationPurpose.Login,
            cancellationToken);
        await loginSecurity.MarkDelivered(challenge.ChallengeId, 0, cancellationToken);
        return new PasswordSignInResult
        {
            PasswordAccepted = true,
            UserId = user.UserId,
            Challenge = WithFlow(challenge, flow),
            FlowCookie = LoginDeviceToken.Serialize(challenge.ChallengeId, secret)
        };
    }

    public async Task<IdentityChallengeDto> ResendCode(
        Guid challengeId,
        string flowSecret,
        string userAgent,
        CancellationToken cancellationToken)
    {
        var code = NewCode();
        var resend = await loginSecurity.ReserveResend(challengeId, flowSecret, userAgent, code, cancellationToken);
        if (!delivery.IsAvailable(resend.Channel))
        {
            throw new VerificationUnavailableException();
        }

        await delivery.SendVerificationCode(
            resend.Channel,
            resend.Destination,
            code,
            VerificationMessage.GetRemainingCodeMinutes(resend.ExpiresAt),
            VerificationPurpose.Login,
            cancellationToken);
        await loginSecurity.MarkDelivered(challengeId, resend.Revision, cancellationToken);
        var maskedDestination = resend.Channel == "email" ? resend.Destination[..1] + "•••" + resend.Destination[resend.Destination.IndexOf('@')..] : "••• " + resend.Destination[^4..];
        return WithFlow(new IdentityChallengeDto(challengeId, resend.Channel, maskedDestination, resend.ExpiresAt), resend.Flow);
    }

    public async Task<SignInSessionAccess> GetSessionAccess(LoginSessionGrant grant, CancellationToken cancellationToken)
    {
        var user = await repository.GetCurrentUser(grant.UserId, cancellationToken);
        if (user is null)
        {
            return new SignInSessionAccess();
        }

        // Recheck current roles and preference after the grant to prevent a concurrent role change bypassing staff MFA.
        var preference = await loginSecurity.GetLoginOtpPreference(user.Id, cancellationToken);
        var canRemember = loginPolicy.CanRemember(user.Roles, preference);
        if (loginPolicy.RequiresCode(user.Roles, preference) && !grant.OtpVerified && !(grant.RememberedDeviceUsed && canRemember))
        {
            await repository.RevokeSession(grant.SessionId, DateTimeOffset.UtcNow, cancellationToken);
            return new SignInSessionAccess
            {
                VerificationRequired = true
            };
        }

        return new SignInSessionAccess
        {
            User = user,
            CanRememberDevice = canRemember
        };
    }

    private static string NewCode() => RandomNumberGenerator.GetInt32(100000, 1000000).ToString(CultureInfo.InvariantCulture);

    private static IdentityChallengeDto WithFlow(IdentityChallengeDto challenge, LoginFlowInfo flow) => new(
        challenge.ChallengeId,
        challenge.Channel,
        challenge.MaskedDestination,
        challenge.ExpiresAt,
        challenge.RequiresVerification,
        flow.ResendAvailableAt,
        flow.FlowExpiresAt,
        flow.CanRememberDevice,
        flow.RememberedDeviceDays);
}
