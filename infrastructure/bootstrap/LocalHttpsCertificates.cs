using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace DisputePortal.Setup;

public static class LocalHttpsCertificates
{
    public static bool IsPrivateAddress(string address)
    {
        if (!IPAddress.TryParse(address, out var ip) || ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
        {
            return false;
        }

        var bytes = ip.GetAddressBytes();
        return bytes[0] == 10
            || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
            || (bytes[0] == 192 && bytes[1] == 168);
    }

    public static void Prepare(InstallationOptions options)
    {
        if (options.HttpsPort is < 1024 or > 65535
            || (options.LocalAddress.Length > 0 && !IsPrivateAddress(options.LocalAddress)))
        {
            throw new InvalidOperationException("Local HTTPS requires a private IPv4 address and a valid nonprivileged port.");
        }

        var privateDirectory = Path.Combine(options.StateDirectory, "https");
        var publicDirectory = Path.Combine(options.SettingsDirectory, "https");
        Directory.CreateDirectory(privateDirectory);
        Directory.CreateDirectory(publicDirectory);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(privateDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.SetUnixFileMode(publicDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }

        var authorityPath = Path.Combine(privateDirectory, "authority.pem");
        var authorityKey = Path.Combine(privateDirectory, "authority.key");
        var now = DateTimeOffset.UtcNow;
        if (!File.Exists(authorityPath) && !File.Exists(authorityKey))
        {
            using var key = RSA.Create(2048);
            var request = new CertificateRequest("CN=Dispute Portal Local CA", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, 0, true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
            request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
            using var authority = request.CreateSelfSigned(now.AddMinutes(-5), now.AddYears(5));
            WriteFile(authorityKey, key.ExportPkcs8PrivateKeyPem(), true);
            WriteFile(authorityPath, authority.ExportCertificatePem(), true);
        }

        using var issuer = X509Certificate2.CreateFromPemFile(authorityPath, authorityKey);
        if (issuer.NotAfter.ToUniversalTime() <= now.AddDays(30).UtcDateTime)
        {
            throw new InvalidOperationException("The local certificate authority needs renewal and must be trusted again on each device.");
        }

        var certificatePath = Path.Combine(publicDirectory, "server.pem");
        var serverKeyPath = Path.Combine(publicDirectory, "server.key");
        var addressPath = Path.Combine(privateDirectory, "address.txt");
        var regenerate = !File.Exists(certificatePath) || !File.Exists(serverKeyPath)
            || !File.Exists(addressPath) || File.ReadAllText(addressPath) != options.LocalAddress;
        if (!regenerate)
        {
            using var certificate = X509Certificate2.CreateFromPemFile(certificatePath, serverKeyPath);
            regenerate = certificate.NotAfter.ToUniversalTime() <= now.AddDays(14).UtcDateTime;
        }

        if (regenerate)
        {
            using var key = RSA.Create(2048);
            var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var names = new SubjectAlternativeNameBuilder();
            names.AddDnsName("localhost");
            names.AddIpAddress(IPAddress.Loopback);
            names.AddIpAddress(IPAddress.IPv6Loopback);
            if (options.LocalAddress.Length > 0)
            {
                names.AddIpAddress(IPAddress.Parse(options.LocalAddress));
            }

            request.CertificateExtensions.Add(names.Build());
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") }, true));
            request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
            using var certificate = request.Create(issuer, now.AddMinutes(-5), now.AddDays(365), RandomNumberGenerator.GetBytes(16));
            WriteFile(serverKeyPath, key.ExportPkcs8PrivateKeyPem());
            WriteFile(certificatePath, certificate.ExportCertificatePem());
            WriteFile(addressPath, options.LocalAddress, true);
        }

        File.WriteAllBytes(Path.Combine(publicDirectory, "authority.cer"), issuer.Export(X509ContentType.Cert));
        WriteFile(Path.Combine(publicDirectory, "authority.pem"), issuer.ExportCertificatePem());
        var fingerprint = issuer.GetCertHashString(HashAlgorithmName.SHA256);
        WriteFile(Path.Combine(publicDirectory, "fingerprint.txt"), fingerprint);
        var host = options.LocalAddress.Length == 0 ? "localhost" : options.LocalAddress;
        var url = $"https://{host}:{options.HttpsPort}";
        WriteFile(Path.Combine(publicDirectory, "install.html"), $$"""
            <!doctype html>
            <html lang="en">
            <head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>Dispute Portal HTTPS</title></head>
            <body>
            <h1>Dispute Portal</h1>
            <p>This local assessment uses a certificate authority generated only for this installation.</p>
            <p><a href="/authority.cer">Download Current Dispute Portal Certificate</a></p>
            <p><a href="#replace-iphone-certificate">Replace An Older iPhone Certificate</a></p>
            <h2 id="replace-iphone-certificate">Replace An Older iPhone Certificate</h2>
            <p>iPhone requires you to remove certificate profiles in Settings. This website cannot remove them automatically.</p>
            <ol>
            <li>Open Settings → General → VPN &amp; Device Management.</li>
            <li>Open the older Dispute Portal Local CA profile. Its certificate details show its fingerprint. If it differs from the current SHA-256 fingerprint below, tap Remove Profile and enter your phone passcode if requested.</li>
            <li>If you cannot distinguish two Dispute Portal certificate profiles, remove those Dispute Portal CA profiles and reinstall the current certificate from this page. Select only the Dispute Portal certificate profiles.</li>
            <li>Return to Safari and tap Download Current Dispute Portal Certificate above.</li>
            <li>Open Settings → Profile Downloaded, or General → VPN &amp; Device Management, and install the downloaded Dispute Portal Local CA.</li>
            <li>Open Settings → General → About → Certificate Trust Settings and enable Full Trust for the newly installed Dispute Portal Local CA.</li>
            <li>Return to Safari and open the secure portal link below.</li>
            </ol>
            <p>The certificate is retained across application restarts and database resets. Replace it only when its fingerprint changes or when replacing an older installation.</p>
            <h2>New iPhone or iPad Installation</h2>
            <p>Download the current certificate, install the downloaded profile in Settings → General → VPN &amp; Device Management, then enable Full Trust in Settings → General → About → Certificate Trust Settings.</p>
            <h2>Android</h2>
            <p>Open Settings → Security → Encryption &amp; Credentials → Install A Certificate → CA Certificate. Menu names vary by manufacturer. Select the downloaded certificate.</p>
            <h2>Windows or macOS</h2>
            <p>Import the certificate into your trusted root certificate authorities. On macOS, use Keychain Access and set the certificate to Always Trust.</p>
            <p>Compare this SHA-256 fingerprint with the one printed by the local launcher before trusting:</p>
            <p><strong>Current SHA-256 Fingerprint</strong></p>
            <p><code>{{fingerprint}}</code></p>
            <p>Current certificate serial number: <code>{{issuer.SerialNumber}}</code></p>
            <p><a href="{{url}}">Open Dispute Portal Securely</a></p>
            <p>Keep the computer and phone on the same Wi-Fi. Remove this local authority from your device when assessment testing is finished.</p>
            </body></html>
            """);
    }

    private static void WriteFile(string path, string text, bool privateFile = false)
    {
        File.WriteAllText(path, text, new UTF8Encoding(false));
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, privateFile ? UnixFileMode.UserRead | UnixFileMode.UserWrite
                : UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        }
    }
}
