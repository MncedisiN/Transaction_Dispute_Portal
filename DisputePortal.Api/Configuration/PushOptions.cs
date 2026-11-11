using System.Net.Mail;
using System.Security.Cryptography;

namespace DisputePortal.Api.Configuration;

public class PushOptions
{
    public bool Enabled { get; set; }
    public string Subject { get; set; } = "";
    public string PublicKey { get; set; } = "";
    public string PrivateKey { get; set; } = "";
    public bool IsConfigured => Enabled && HasValidConfiguration();

    public void ValidatePushConfiguration()
    {
        if (Enabled && !HasValidConfiguration())
        {
            throw new InvalidOperationException("Enabled push delivery requires a valid mailto contact and a matching P-256 VAPID key pair.");
        }
    }

    private bool HasValidConfiguration()
    {
        try
        {
            if (Subject.Contains('"')
                || Subject.Contains('\\')
                || Subject.Any(char.IsControl)
                || !Subject.StartsWith("mailto:", StringComparison.Ordinal)
                || !MailAddress.TryCreate(Subject[7..], out var contact)
                || contact.Address != Subject[7..])
            {
                return false;
            }

            var publicBytes = DecodeBase64Url(PublicKey);
            var privateBytes = DecodeBase64Url(PrivateKey);
            if (publicBytes.Length != 65 || publicBytes[0] != 4 || privateBytes.Length != 32)
            {
                return false;
            }

            var point = new ECPoint
            {
                X = publicBytes[1..33],
                Y = publicBytes[33..65]
            };
            using var signer = ECDsa.Create(new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = point,
                D = privateBytes
            });
            using var verifier = ECDsa.Create(new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = point
            });
            ReadOnlySpan<byte> challenge = "Resolve push configuration"u8;
            return verifier.VerifyData(challenge, signer.SignData(challenge, HashAlgorithmName.SHA256), HashAlgorithmName.SHA256);
        }
        catch (Exception error) when (error is FormatException or CryptographicException or ArgumentException)
        {
            return false;
        }
    }

    public static byte[] DecodeBase64Url(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Any(character => !char.IsAsciiLetterOrDigit(character)
            && character is not '-' and not '_'))
        {
            throw new FormatException("Invalid base64url value.");
        }

        var decoded = Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/').PadRight((value.Length + 3) / 4 * 4, '='));
        if (Convert.ToBase64String(decoded).TrimEnd('=').Replace('+', '-').Replace('/', '_') != value)
        {
            throw new FormatException("Invalid base64url value.");
        }

        return decoded;
    }
}
