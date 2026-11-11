using DisputePortal.Api.Models;
using Microsoft.AspNetCore.Identity;
using DisputePortal.Api.Interfaces.Security;

namespace DisputePortal.Api.Service.Security;

public class PortalPasswordVerifier(IPasswordHasher<PortalPasswordUser> passwords) : IPasswordVerifier
{
    public bool Verify(Guid userId, string passwordHash, string suppliedPassword) => passwords.VerifyHashedPassword(
        new PortalPasswordUser(userId),
        passwordHash,
        suppliedPassword) != PasswordVerificationResult.Failed;
}
