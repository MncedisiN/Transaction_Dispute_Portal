using Microsoft.AspNetCore.DataProtection;
using DisputePortal.Api.Interfaces.Security;

namespace DisputePortal.Api.Service.Security;

public class SecretProtector(IDataProtectionProvider provider) : ISecretProtector
{
    // The persisted protection purpose is retained to decrypt existing contact, identity and evidence-related payloads.
    // It is an encryption compatibility identifier, not an authenticator implementation.
    private readonly IDataProtector protector = provider.CreateProtector("DisputePortal.Authenticator.v1");

    public string Protect(string secret) => protector.Protect(secret);

    public string Unprotect(string secret) => protector.Unprotect(secret);

    public byte[] ProtectBytes(byte[] content, string context) => protector.CreateProtector(context).Protect(content);

    public byte[] UnprotectBytes(byte[] content, string context) => protector.CreateProtector(context).Unprotect(content);
}
