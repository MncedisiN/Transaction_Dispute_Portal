using System.Data;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text.Json;
using Dapper;
using DisputePortal.Platform.Contracts;
using DisputePortal.Platform.Configuration;
using DisputePortal.Platform.Infrastructure;
using DisputePortal.Platform.Payments;
using Xunit;
using DisputePortal.Platform.Infrastructure.Models;

namespace DisputePortal.Platform.Tests;

public sealed class SecurityAndPolicyTests
{
    private static PlatformSettings Settings => JsonSerializer.Deserialize<PlatformSettings>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "seed-settings.json")),
        DomainEvent.Json)!;

    [Theory]
    [InlineData("#", "payment.completed", true)]
    [InlineData("payment.*", "payment.completed", true)]
    [InlineData("payment.*", "payment.completed.extra", false)]
    [InlineData("ledger.#", "ledger.transaction.posted", true)]
    [InlineData("account.created", "account.activated", false)]
    public void Topic_patterns_follow_broker_routing(string pattern, string type, bool expected) => Assert.Equal(
        expected,
        EventRouting.Matches(pattern, type));

    [Fact]
    public void Signing_remains_valid_across_repeated_disposed_key_instances()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var options = TestMigrationConfiguration.ApplyServiceMigrationSettings(new ServiceOptions
        {
            Name = "identity",
            SigningKey = key.ExportPkcs8PrivateKeyPem()
        });
        var ids = new HashSet<string>();
        for (var i = 0; i < 100; i++)
        {
            var token = new JwtSecurityTokenHandler().ReadJwtToken(ServiceSecurity.Token(options, "accounts", null));
            Assert.Equal("ES256", token.Header.Alg);
            Assert.True(ids.Add(token.Id));
        }
    }

    [Fact]
    public void Database_UTC_timestamps_map_to_immutable_records()
    {
        PlatformMapping.InitializeDapperMappings();
        var data = new DataTable();
        data.Columns.Add("At", typeof(DateTime));
        data.Rows.Add(new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc));
        using var reader = data.CreateDataReader();
        var result = reader.Parse<Timestamp>().Single();
        Assert.Equal(TimeSpan.Zero, result.At.Offset);
        Assert.Equal(3, result.At.Hour);
    }

    [Fact]
    public void Seed_countries_and_all_South_African_provinces_validate()
    {
        SettingsPolicy.ValidatePlatformSettings(Settings);
        Assert.Equal(9, Settings.Countries.Single(c => c.Code == "ZA").Regions.Length);
        Assert.Equal(9, Settings.Countries.Length);
    }

    [Fact]
    public void South_African_validation_cannot_be_applied_to_another_issuer()
    {
        var settings = Settings;
        var foreign = settings.Countries.Single(c => c.Code == "ZW");
        var bad = new global::DisputePortal.Platform.Contracts.CountryDefinition(foreign)
        {
            Documents = [new global::DisputePortal.Platform.Contracts.IdentityDocumentDefinition
            {
            Key = "national-id",
            Label = "National ID",
            Pattern = "^[0-9]{13}$",
            Validator = "south-african-id"
        }

            ]
        };
        Assert.Throws<PlatformException>(() => SettingsPolicy.ValidatePlatformSettings(new global::DisputePortal.Platform.Contracts.PlatformSettings(settings) { Countries = settings.Countries.Select(c => c.Code == "ZW" ? bad : c).ToArray() }));
    }

    [Fact]
    public void Only_the_default_currency_can_be_saved_for_a_country()
    {
        var settings = Settings;
        var countries = settings.Countries.Select(country => country.Code == "ZA" ? new global::DisputePortal.Platform.Contracts.CountryDefinition(country) { Currencies = ["ZAR", "USD"] } : country).ToArray();
        Assert.Throws<PlatformException>(() => SettingsPolicy.ValidatePlatformSettings(new global::DisputePortal.Platform.Contracts.PlatformSettings(settings) { Countries = countries }));
    }

    [Theory]
    [InlineData("0.001")]
    [InlineData("100000000000000000000")]
    [InlineData("-1")]
    public void Money_policy_rejects_precision_and_out_of_range_amounts(string input)
    {
        var user = Guid.NewGuid();
        var a = new AccountRecord
        {
            Id = Guid.NewGuid(),
            UserId = user,
            Number = "123456789012",
            Type = "Cheque",
            CountryCode = "ZA",
            Currency = "ZAR",
            Status = "Active",
            LedgerBalance = 100,
            ReservedBalance = 0,
            Version = 0,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var r = new PaymentQuoteRequest("Deposit", a.Id, null, decimal.Parse(input, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Throws<PlatformException>(() => PaymentPolicy.ValidatePaymentPolicy(r, user, a, null, Settings));
    }

    private class Timestamp
    {
        public DateTimeOffset At { get; set; }
    }
}
