using System.Security.Cryptography;
using System.Text;
using DisputePortal.Api.Configuration;
using DisputePortal.Api.Interfaces.Security;

namespace DisputePortal.Api.Service.Security;

public class OtpHasher(InfobipOptions options) : IOtpHasher
{
    public string Hash(Guid challengeId, string code)
    {
        if (options.OtpHashKey.Length < 32)
        {
            throw new InvalidOperationException("Configure Infobip:OtpHashKey with at least 32 random characters.");
        }

        return Convert.ToHexString(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(options.OtpHashKey),
            Encoding.UTF8.GetBytes($"{challengeId:N}:{code}")));
    }

    public bool Matches(Guid challengeId, string code, string expectedHash)
    {
        if (code.Length != 6 || code.Any(c => c < '0' || c > '9'))
        {
            return false;
        }

        var actual = Encoding.UTF8.GetBytes(Hash(challengeId, code));
        return CryptographicOperations.FixedTimeEquals(actual, Encoding.UTF8.GetBytes(expectedHash));
    }
}
