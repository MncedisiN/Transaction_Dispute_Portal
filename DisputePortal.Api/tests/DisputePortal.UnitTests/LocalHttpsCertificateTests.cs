using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using DisputePortal.Setup;
using Xunit;

namespace DisputePortal.UnitTests;

public class LocalHttpsCertificateTests
{
    [Fact]
    public void Certificates_trust_only_the_installation_authority_and_survive_address_changes()
    {
        var directory = Directory.CreateTempSubdirectory("portal-https-");
        try
        {
            var options = new InstallationOptions
            {
                StateDirectory = Path.Combine(directory.FullName, "state"),
                SettingsDirectory = Path.Combine(directory.FullName, "settings"),
                LocalAddress = "192.168.10.109"
            };
            LocalHttpsCertificates.Prepare(options);
            var path = Path.Combine(options.SettingsDirectory, "https");
            using var authority = X509CertificateLoader.LoadCertificateFromFile(Path.Combine(path, "authority.cer"));
            using var server = X509Certificate2.CreateFromPemFile(Path.Combine(path, "server.pem"), Path.Combine(path, "server.key"));
            using var chain = new X509Chain();
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.CustomTrustStore.Add(authority);
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            Assert.True(chain.Build(server));
            Assert.True(server.HasPrivateKey);
            Assert.True(server.MatchesHostname("localhost"));
            Assert.True(server.MatchesHostname(options.LocalAddress));
            Assert.False(server.MatchesHostname("192.168.10.110"));
            Assert.True(server.NotAfter - server.NotBefore < TimeSpan.FromDays(366));
            Assert.False(File.Exists(Path.Combine(path, "authority.key")));
            var serverHash = server.GetCertHashString(HashAlgorithmName.SHA256);
            LocalHttpsCertificates.Prepare(options);
            using var restarted = X509Certificate2.CreateFromPemFile(Path.Combine(path, "server.pem"), Path.Combine(path, "server.key"));
            Assert.Equal(serverHash, restarted.GetCertHashString(HashAlgorithmName.SHA256));
            options.LocalAddress = "10.0.0.23";
            LocalHttpsCertificates.Prepare(options);
            using var changed = X509Certificate2.CreateFromPemFile(Path.Combine(path, "server.pem"), Path.Combine(path, "server.key"));
            using var sameAuthority = X509CertificateLoader.LoadCertificateFromFile(Path.Combine(path, "authority.cer"));
            Assert.True(changed.MatchesHostname(options.LocalAddress));
            Assert.Equal(authority.Thumbprint, sameAuthority.Thumbprint);
            Assert.True(chain.Build(changed));
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("169.254.1.1")]
    [InlineData("example.com")]
    [InlineData("127.0.0.1")]
    public void Public_or_unroutable_phone_addresses_are_rejected(string address)
    {
        Assert.False(LocalHttpsCertificates.IsPrivateAddress(address));
    }
}
