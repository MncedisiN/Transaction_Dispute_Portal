using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using DisputePortal.Setup;
using Microsoft.AspNetCore.Identity;
using Xunit;

namespace DisputePortal.UnitTests;

public class AssessmentInstallerTests
{
    [Fact]
    public void Fresh_installation_uses_dotnet_password_hashing_and_valid_service_keys()
    {
        using var installation = new TestInstallation();
        var result = AssessmentInstaller.Initialize(installation.Options);
        var login = Assert.Single(result.InitialAccess);
        Assert.Equal("admin@assessment.local", login.Email);
        Assert.Equal(["super_admin"], login.Roles);

        var settings = installation.ReadSettings("identity");
        var hash = settings["DevelopmentSeed"]!["Users"]![0]!["PasswordHash"]!.GetValue<string>();
        var hasher = new PasswordHasher<InitialAdministrator>();
        var user = new InitialAdministrator { Email = login.Email };
        Assert.Equal(PasswordVerificationResult.Success, hasher.VerifyHashedPassword(user, hash, login.Password));
        Assert.Equal(PasswordVerificationResult.Failed, hasher.VerifyHashedPassword(user, hash, "incorrect-password"));
        Assert.DoesNotContain(login.Password, File.ReadAllText(installation.StatePath));
        Assert.False(settings["Authentication"]!["LoginOtpEnabled"]!.GetValue<bool>());
        Assert.True(settings["Assessment"]!["SkipContactVerification"]!.GetValue<bool>());
        foreach (var service in new[] { "identity", "ledger", "transactions" })
        {
            Assert.True(installation.ReadSettings(service)["Assessment"]!["PopulateAccounts"]!.GetValue<bool>());
        }
        Assert.False(settings["Infobip"]!["Enabled"]!.GetValue<bool>());
        Assert.False(settings["Platform"]!["BrokerEnabled"]!.GetValue<bool>());
        Assert.Equal(11, result.ServiceCount);

        using var privateKey = ECDsa.Create();
        using var publicKey = ECDsa.Create();
        privateKey.ImportFromPem(settings["Platform"]!["SigningKey"]!.GetValue<string>());
        publicKey.ImportFromPem(settings["Platform"]!["PublicKeys"]!["identity"]!.GetValue<string>());
        var message = Encoding.UTF8.GetBytes("installation-key-verification");
        Assert.True(publicKey.VerifyData(message, privateKey.SignData(message, HashAlgorithmName.SHA256), HashAlgorithmName.SHA256));
    }

    [Fact]
    public void Restart_preserves_credentials_and_does_not_print_passwords_again()
    {
        using var installation = new TestInstallation();
        var first = AssessmentInstaller.Initialize(installation.Options);
        var stateBefore = File.ReadAllText(installation.StatePath);
        var settingsBefore = installation.ReadSettings("identity").ToJsonString();
        var restart = AssessmentInstaller.Initialize(installation.Options);
        Assert.Empty(restart.InitialAccess);
        Assert.Equal(first.Fingerprint, restart.Fingerprint);
        Assert.Equal(stateBefore, File.ReadAllText(installation.StatePath));
        Assert.Equal(settingsBefore, installation.ReadSettings("identity").ToJsonString());
    }

    [Fact]
    public void Damaged_installation_identity_is_not_replaced()
    {
        using var installation = new TestInstallation();
        AssessmentInstaller.Initialize(installation.Options);
        File.WriteAllText(installation.StatePath, "{\"version\":999}");
        Assert.Throws<InvalidOperationException>(() => AssessmentInstaller.Initialize(installation.Options));
        Assert.Equal("{\"version\":999}", File.ReadAllText(installation.StatePath));
    }

    [Fact]
    public void Missing_identity_does_not_rotate_credentials_for_existing_databases()
    {
        using var installation = new TestInstallation();
        AssessmentInstaller.Initialize(installation.Options);
        var before = installation.ReadSettings("identity").ToJsonString();
        File.Delete(installation.StatePath);
        Assert.Throws<InvalidOperationException>(() => AssessmentInstaller.Initialize(installation.Options));
        Assert.Equal(before, installation.ReadSettings("identity").ToJsonString());
    }

    [Theory]
    [InlineData("SqlServer")]
    [InlineData("PostgreSql")]
    public void Api_provider_is_selectable_while_financial_databases_stay_postgresql(string provider)
    {
        using var installation = new TestInstallation();
        installation.Options.ApiDatabaseProvider = provider;
        AssessmentInstaller.Initialize(installation.Options);
        Assert.Equal(provider, installation.ReadSettings("identity")["Database"]!["Provider"]!.GetValue<string>());
        Assert.Equal("PostgreSql", installation.ReadSettings("ledger")["Platform"]!["DatabaseProvider"]!.GetValue<string>());
        installation.Options.ApiDatabaseProvider = provider == "SqlServer" ? "PostgreSql" : "SqlServer";
        Assert.Throws<InvalidOperationException>(() => AssessmentInstaller.Initialize(installation.Options));
    }

    [Fact]
    public void Messaging_configuration_can_change_without_overwriting_security_identity()
    {
        using var installation = new TestInstallation();
        installation.Options.PrivateSettingsFile = Path.Combine(installation.Root, "messaging.json");
        installation.Options.ExternalMessagesEnabled = true;
        File.WriteAllText(installation.Options.PrivateSettingsFile,
            """{"Infobip":{"Enabled":true,"EmailApiKey":"test-key","EmailFromAddress":"noreply@example.test"}}""");
        AssessmentInstaller.Initialize(installation.Options);
        var before = installation.ReadSettings("identity");
        Assert.True(before["Infobip"]!["Enabled"]!.GetValue<bool>());
        Assert.True(before["Push"]!["Enabled"]!.GetValue<bool>());
        File.WriteAllText(installation.Options.PrivateSettingsFile,
            """{"Infobip":{"Enabled":true,"EmailApiKey":"updated-test-key"}}""");
        AssessmentInstaller.Initialize(installation.Options);
        var after = installation.ReadSettings("identity");
        Assert.Equal("updated-test-key", after["Infobip"]!["EmailApiKey"]!.GetValue<string>());
        Assert.Equal(before["Infobip"]!["OtpHashKey"]!.GetValue<string>(), after["Infobip"]!["OtpHashKey"]!.GetValue<string>());
        Assert.Equal(before["Platform"]!["ConnectionString"]!.GetValue<string>(), after["Platform"]!["ConnectionString"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("{\"Infobip\":{\"OtpHashKey\":\"invalid\"}}")]
    [InlineData("{\"Infobip\":{\"Enabled\":\"true\"}}")]
    [InlineData("{\"Infobip\":[]}")]
    public void Private_messaging_cannot_override_security_settings(string json)
    {
        using var installation = new TestInstallation();
        installation.Options.PrivateSettingsFile = Path.Combine(installation.Root, "messaging.json");
        File.WriteAllText(installation.Options.PrivateSettingsFile, json);
        Assert.Throws<InvalidOperationException>(() => AssessmentInstaller.Initialize(installation.Options));
        Assert.False(File.Exists(installation.StatePath));
    }

    private class TestInstallation : IDisposable
    {
        public string Root { get; } = Directory.CreateTempSubdirectory("portal-installation-").FullName;
        public string StatePath => Path.Combine(Options.StateDirectory, "identity.json");
        public InstallationOptions Options { get; }

        public TestInstallation()
        {
            Options = new InstallationOptions
            {
                StateDirectory = Path.Combine(Root, "state"),
                SettingsDirectory = Path.Combine(Root, "settings")
            };
        }

        public JsonObject ReadSettings(string service) => JsonNode.Parse(
            File.ReadAllText(Path.Combine(Options.SettingsDirectory, service, "settings.json")))!.AsObject();

        public void Dispose() => Directory.Delete(Root, true);
    }
}
