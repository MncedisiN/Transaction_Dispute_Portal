namespace DisputePortal.Api.Interfaces.Security;

public interface ISecretProtector
{
    string Protect(string secret);

    string Unprotect(string secret); 
    byte[] ProtectBytes(byte[] content, string context) =>
        System.Text.Encoding.UTF8.GetBytes(Protect(context + ":" + Convert.ToBase64String(content)));

    byte[] UnprotectBytes(byte[] content, string context)
    {
        var value = Unprotect(System.Text.Encoding.UTF8.GetString(content));
        var prefix = context + ":";
        if (!value.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw new System.Security.Cryptography.CryptographicException("Evidence protection context mismatch.");
        }

        return Convert.FromBase64String(value[prefix.Length..]);
    }
}
