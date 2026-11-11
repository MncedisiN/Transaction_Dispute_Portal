using System.Security.Cryptography;

namespace DisputePortal.Api.Models.Domain;

public static class LoginDeviceToken
{
    public static string NewSecret() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace(
        '/',
        '_');

    public static string Serialize(Guid id, string secret) => id.ToString("N") + "." + secret;

    public static bool TryParse(string? value, out Guid id, out string secret)
    {
        id = Guid.Empty;
        secret = "";
        if (value is null || value.Length != 76 || value[32] != '.' || !Guid.TryParseExact(value[..32], "N", out id))
        {
            return false;
        }

        secret = value[33..];
        return secret.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
    }
}
