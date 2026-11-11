using System.Text.Json;
using DisputePortal.Platform.Configuration;
using DisputePortal.Platform.Contracts;
using DisputePortal.Platform.Infrastructure;
using Xunit;
using DisputePortal.Platform.Infrastructure.Models;

namespace DisputePortal.Platform.Tests;

public class RegistrationRequirementTests
{
    private static PlatformSettings Settings => JsonSerializer.Deserialize<PlatformSettings>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "seed-settings.json")),
        DomainEvent.Json)!;

    [Fact]
    public void Published_requirements_keep_addresses_optional_and_contact_verification_mandatory()
    {
        var settings = Settings;
        Assert.False(settings.Registration.RequireAddress);
        SettingsPolicy.ValidatePlatformSettings(settings);
        var stale = new global::DisputePortal.Platform.Contracts.PlatformSettings(settings)
        {
            Registration = new global::DisputePortal.Platform.Contracts.RegistrationRequirements(settings.Registration)
            {
                RequireAddress = true
            }
        };
        Assert.Equal(400, Assert.Throws<PlatformException>(() => SettingsPolicy.ValidatePlatformSettings(stale)).Status);
        var unsafeContact = new global::DisputePortal.Platform.Contracts.PlatformSettings(settings)
        {
            Registration = new global::DisputePortal.Platform.Contracts.RegistrationRequirements(settings.Registration)
            {
                RequireVerification = false
            }
        };
        Assert.Equal(400, Assert.Throws<PlatformException>(() => SettingsPolicy.ValidatePlatformSettings(unsafeContact)).Status);
    }

    [Fact]
    public void Missing_registration_requirements_are_a_validation_failure()
    {
        Assert.Equal(
            400,
            Assert.Throws<PlatformException>(() => SettingsPolicy.ValidatePlatformSettings(new global::DisputePortal.Platform.Contracts.PlatformSettings(Settings) { Registration = null! })).Status);
    }
}
