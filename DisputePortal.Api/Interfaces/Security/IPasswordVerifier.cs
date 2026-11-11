namespace DisputePortal.Api.Interfaces.Security;

public interface IPasswordVerifier
{
    bool Verify(Guid userId, string passwordHash, string suppliedPassword);
}
