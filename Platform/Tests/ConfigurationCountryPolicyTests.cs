using System.Text.Json;
using DisputePortal.Platform.Configuration;
using DisputePortal.Platform.Contracts;
using DisputePortal.Platform.Infrastructure;
using Xunit;
using DisputePortal.Platform.Infrastructure.Models;

namespace DisputePortal.Platform.Tests;

public class ConfigurationCountryPolicyTests
{
    private static PlatformSettings Settings => JsonSerializer.Deserialize<PlatformSettings>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "seed-settings.json")),
        DomainEvent.Json)!;

    [Theory]
    [InlineData("ZA", "ZAR")]
    [InlineData("ZW", "ZWG")]
    public void Saved_country_is_the_single_source_of_new_account_currency(string country, string currency)
    {
        var settings = new global::DisputePortal.Platform.Contracts.PlatformSettings(Settings)
        {
            OperatingCountry = country
        };
        SettingsPolicy.ValidatePlatformSettings(settings);
        Assert.Equal(currency, CountryCurrencyPolicy.Current(settings).DefaultCurrency);
        Assert.True(CountryCurrencyPolicy.IsCurrent(settings, country, currency));
        Assert.All(settings.Countries, definition => Assert.Equal([definition.DefaultCurrency], definition.Currencies));
    }

    [Fact]
    public void ZiG_keeps_the_real_ISO_currency_code_and_display_metadata_separate()
    {
        var zig = Settings.Currencies.Single(currency => currency.Code == "ZWG");
        Assert.Equal("ZiG", zig.Name);
        Assert.Equal("ZiG", zig.Symbol);
        Assert.Equal(2, zig.Decimals);
        Assert.DoesNotContain(Settings.Currencies, currency => currency.Code == "ZiG");
    }

    [Theory]
    [InlineData("ZA", "USD")]
    [InlineData("ZW", "ZAR")]
    [InlineData("ZW", "ZWG")]
    public void Residence_or_a_historical_currency_does_not_override_the_saved_country(string jurisdiction, string currency) =>
        Assert.False(CountryCurrencyPolicy.IsCurrent(Settings, jurisdiction, currency));

    [Fact]
    public void Disabling_the_saved_country_is_rejected_without_silently_selecting_another()
    {
        var settings = Settings;
        settings = new global::DisputePortal.Platform.Contracts.PlatformSettings(settings)
        {
            Countries = settings.Countries.Select(country => country.Code == settings.OperatingCountry ? new global::DisputePortal.Platform.Contracts.CountryDefinition(country) { Active = false } : country).ToArray()
        };
        Assert.Throws<PlatformException>(() => SettingsPolicy.ValidatePlatformSettings(settings));
        Assert.Throws<InvalidOperationException>(() => CountryCurrencyPolicy.Current(settings));
    }

    [Theory]
    [InlineData("identity", "DisputeFinance", true)]
    [InlineData("identity", "Connection", true)]
    [InlineData("accounts", "Account", true)]
    [InlineData("payments", "Payment", true)]
    [InlineData("scheduling", "Reversal", true)]
    [InlineData("transactions", "Reversal", true)]
    [InlineData("ledger", "Import", true)]
    [InlineData("identity", "Payment", false)]
    [InlineData("payments", "Import", false)]
    [InlineData("accounts", "DisputeFinance", false)]
    [InlineData("reporting", "Connection", false)]
    [InlineData("notifications", "Account", false)]
    [InlineData("configuration", "Reversal", false)]
    public void Only_the_owning_service_can_start_its_country_operation(string caller, string kind, bool allowed) => Assert.Equal(
        allowed,
        CountryOperationStore.CanBegin(caller, kind));

    [Theory]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, true, false)]
    [InlineData(false, false, false, true)]
    public void Each_financial_blocker_independently_prevents_a_country_change(bool balances, bool reserves, bool refunds, bool operations) =>
        Assert.False(new CountryChangeEligibility
    {
        HasNonzeroBalances = balances,
        HasReserves = reserves,
        HasPendingRefunds = refunds,
        HasUnfinishedOperations = operations
    }.Allowed);
}
