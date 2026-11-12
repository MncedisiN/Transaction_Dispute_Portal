using System.Security.Cryptography.X509Certificates;
using System.Text;
using DisputePortal.Setup;
using Xunit;
namespace DisputePortal.UnitTests;
public class LocalHttpsCertificateTests
{
 [Fact]
 public void Restart_preserves_trusted_authority_and_renews_leaf_when_revocation_address_changes()
 {
  var root=Directory.CreateTempSubdirectory("portal-https-regression-").FullName;
  try {
   var options=new InstallationOptions { StateDirectory=Path.Combine(root,"state"),SettingsDirectory=Path.Combine(root,"settings"),LocalAddress="192.168.10.109",HttpsPort=5443,CertificatePort=8081 };
   LocalHttpsCertificates.Prepare(options);
   var publicDirectory=Path.Combine(options.SettingsDirectory,"https");
   using var authority=X509Certificate2.CreateFromPem(File.ReadAllText(Path.Combine(publicDirectory,"authority.pem")));
   using var first=X509Certificate2.CreateFromPem(File.ReadAllText(Path.Combine(publicDirectory,"server.pem")));
   Assert.NotNull(first.Extensions["2.5.29.31"]);
   Assert.Contains("http://192.168.10.109:8081/portal.crl",Encoding.ASCII.GetString(first.Extensions["2.5.29.31"]!.RawData));
   Assert.NotEmpty(File.ReadAllBytes(Path.Combine(publicDirectory,"portal.crl")));
   LocalHttpsCertificates.Prepare(options);
   using var restarted=X509Certificate2.CreateFromPem(File.ReadAllText(Path.Combine(publicDirectory,"server.pem")));
   Assert.Equal(first.Thumbprint,restarted.Thumbprint);
   options.CertificatePort=8091;
   LocalHttpsCertificates.Prepare(options);
   using var renewed=X509Certificate2.CreateFromPem(File.ReadAllText(Path.Combine(publicDirectory,"server.pem")));
   using var retainedAuthority=X509Certificate2.CreateFromPem(File.ReadAllText(Path.Combine(publicDirectory,"authority.pem")));
   Assert.NotEqual(first.Thumbprint,renewed.Thumbprint);
   Assert.Equal(authority.Thumbprint,retainedAuthority.Thumbprint);
   Assert.Contains("http://192.168.10.109:8091/portal.crl",Encoding.ASCII.GetString(renewed.Extensions["2.5.29.31"]!.RawData));
  } finally {
   if(Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase))Directory.Delete(root,true);
  }
 }
}
