using DisputePortal.Api.Service.ExternalServices;
using DisputePortal.Api.Models.Security;
using System.Globalization;
using System.Security.Cryptography;
using DisputePortal.Api.Models.Exceptions;
using DisputePortal.Api.Models.Requests;
using DisputePortal.Api.Models.Responses;
using DisputePortal.Api.Interfaces.Repositories;
using DisputePortal.Api.Interfaces.Accounts;
using DisputePortal.Api.Interfaces.Security;

namespace DisputePortal.Api.Service.Accounts;

public class AccountClosureService(
    IAccountManagementRepository accounts,
    IProfileRepository profiles,
    IAuthenticationRepository authentication,
    IIdentityVerificationRepository identity,
    IVerificationDelivery delivery,
    IPasswordVerifier passwords) : IAccountClosureService
{
    public async Task<DeletionChallengeDto> StartAccountClosure(
        Guid userId,
        StartDeletionRequest request,
        CancellationToken cancellationToken)
    {
        var profile = await profiles.GetProfile(userId, cancellationToken);
        var contact = string.IsNullOrEmpty(profile.Email) ? profile.Phone ?? "" : profile.Email;
        var login = await authentication.FindLogin(contact.ToUpperInvariant(), cancellationToken);
        if (login is null || login.UserId != userId || !passwords.Verify(login.UserId, login.PasswordHash, request.CurrentPassword))
        {
            throw new PortalValidationException("Check your current password.", "password");
        }

        var verified = await identity.FindIdentity(login.LoginIdentifier, cancellationToken);
        var destination = verified?.VerifiedContact();
        if (destination is null)
        {
            throw new PortalValidationException("Verify an email or mobile number before requesting deletion.", "contact");
        }

        var (channel, address) = destination.Value;
        if (!delivery.IsAvailable(channel))
        {
            throw new VerificationUnavailableException();
        }

        var code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString(CultureInfo.InvariantCulture);
        var challenge = await accounts.ReserveDeletionChallenge(userId, address, code, cancellationToken);
        try
        {
            await delivery.SendVerificationCode(
                channel,
                address,
                code,
                VerificationMessage.GetRemainingCodeMinutes(challenge.ExpiresAt),
                VerificationPurpose.AccountDeletion,
                cancellationToken);
            await accounts.SetDeletionChallengeReady(userId, challenge.Id, true, cancellationToken);
        }
        catch
        {
            // A disconnected request must not leave an undelivered challenge usable.
            await accounts.SetDeletionChallengeReady(userId, challenge.Id, false, CancellationToken.None);
            throw;
        }

        return challenge;
    }
}
