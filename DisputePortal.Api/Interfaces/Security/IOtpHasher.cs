namespace DisputePortal.Api.Interfaces.Security;

public interface IOtpHasher
{
    string Hash(Guid challengeId, string code);

    bool Matches(Guid challengeId, string code, string expectedHash);
}
