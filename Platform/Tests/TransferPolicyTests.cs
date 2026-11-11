using System.Text.Json;
using DisputePortal.Platform.Contracts;
using DisputePortal.Platform.Infrastructure;
using DisputePortal.Platform.Payments;
using Xunit;
using DisputePortal.Platform.Infrastructure.Models;

namespace DisputePortal.Platform.Tests;

public sealed class TransferPolicyTests
{
    private static PlatformSettings Settings => JsonSerializer.Deserialize<PlatformSettings>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "seed-settings.json")),
        DomainEvent.Json)!;

    private static readonly Guid Owner = Guid.NewGuid();

    private static AccountRecord Account(decimal balance = 100, decimal reserved = 0) =>
        new global::DisputePortal.Platform.Contracts.AccountRecord
    {
        Id = Guid.NewGuid(),
        UserId = Owner,
        Number = "123456789012",
        Type = "Cheque",
        CountryCode = "ZA",
        Currency = "ZAR",
        Status = "Active",
        LedgerBalance = balance,
        ReservedBalance = reserved,
        Version = 0,
        CreatedAt = DateTimeOffset.UtcNow
    };

    private static PlatformSettings Policy(decimal fixedFee = 0, decimal percent = 0) =>
        new global::DisputePortal.Platform.Contracts.PlatformSettings(Settings)
    {
        FinancialRules = Settings.FinancialRules.Select(r => r.Country == "ZA" && r.Currency == "ZAR" ? new global::DisputePortal.Platform.Contracts.FinancialRule(r)
        {
            FixedFee = fixedFee,
            PercentageFee = percent
        } : r).ToArray()
    };

    [Theory]
    [InlineData("100", "0", "0", "0", true)]
    [InlineData("100", "0", "1", "0", false)]
    [InlineData("99", "1", "1", "0", false)]
    [InlineData("98", "1", "1", "0", true)]
    [InlineData("99", "0", "0", "2", false)]
    public void Available_funds_include_reservations_and_fees(string amount, string reserved, string fixedFee, string percent, bool allowed)
    {
        static decimal D(string value) => decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
        var source = Account(100, D(reserved));
        var target = Account();
        var request = new PaymentQuoteRequest("Transfer", source.Id, target.Id, D(amount));
        if (allowed)
        {
            Assert.True(PaymentPolicy.ValidatePaymentPolicy(request, Owner, source, target, Policy(D(fixedFee), D(percent))) >= 0);
        }
        else
        {
            var error = Assert.Throws<PlatformException>(() => PaymentPolicy.ValidatePaymentPolicy(
                request,
                Owner,
                source,
                target,
                Policy(D(fixedFee), D(percent))));
            Assert.Equal(409, error.Status);
            Assert.Equal("insufficient_funds", error.Code);
        }
    }

    [Fact]
    public void Internal_transfers_require_an_owned_destination()
    {
        var source = Account();
        var target = new global::DisputePortal.Platform.Contracts.AccountRecord(Account())
        {
            UserId = Guid.NewGuid()
        };
        var error = Assert.Throws<PlatformException>(() => PaymentPolicy.ValidatePaymentPolicy(
            new("Transfer", source.Id, target.Id, 10),
            Owner,
            source,
            target,
            Policy()));
        Assert.Equal("recipient_invalid", error.Code);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1.001")]
    public void Transfer_amount_precision_and_positive_limits_apply(string amount)
    {
        var source = Account();
        var target = Account();
        Assert.Equal(
            "amount_invalid",
            Assert.Throws<PlatformException>(() => PaymentPolicy.ValidatePaymentPolicy(
                new("Transfer", source.Id, target.Id, decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture)),
                Owner,
                source,
                target,
                Policy())).Code);
    }

    [Theory]
    [InlineData("1", "0.005", "0")]
    [InlineData("1", "0.015", "0.02")]
    public void Fees_use_decimal_half_even_rounding(string amount, string fee, string expected)
    {
        static decimal D(string value) => decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
        var source = Account();
        var target = Account();
        Assert.Equal(
            D(expected),
            PaymentPolicy.ValidatePaymentPolicy(new("Transfer", source.Id, target.Id, D(amount)), Owner, source, target, Policy(D(fee))));
    }

    [Fact]
    public void Exact_amount_fields_roundtrip_under_strict_contract_json()
    {
        var quote = new PaymentQuote
        {
            Id = Guid.NewGuid(),
            UserId = Owner,
            Kind = "Transfer",
            AccountId = Guid.NewGuid(),
            RecipientAccountId = Guid.NewGuid(),
            Amount = 90071992547409.99m,
            Currency = "ZAR",
            Fee = 0.01m,
            TargetAmount = 1,
            TargetCurrency = "USD",
            FxQuoteId = null,
            SettingsVersion = 1,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(1)
        };
        var json = JsonSerializer.Serialize(quote, DomainEvent.Json);
        Assert.Contains("90071992547410.00", json);
        Assert.Equivalent(quote, JsonSerializer.Deserialize<PaymentQuote>(json, DomainEvent.Json), strict: true);
        var settings = Policy();
        var restored = JsonSerializer.Deserialize<PlatformSettings>(JsonSerializer.Serialize(settings, DomainEvent.Json), DomainEvent.Json)!;
        Assert.Equal(
            JsonSerializer.Serialize(settings.FinancialRules, DomainEvent.Json),
            JsonSerializer.Serialize(restored.FinancialRules, DomainEvent.Json));
        var account = Account();
        Assert.Equivalent(
            account,
            JsonSerializer.Deserialize<AccountRecord>(JsonSerializer.Serialize(account, DomainEvent.Json), DomainEvent.Json),
            strict: true);
    }

    [Fact]
    public void Existing_journal_hash_input_does_not_gain_default_policy_version()
    {
        var journal = new PostJournalRequest
        {
            TransactionId = Guid.NewGuid(),
            UserId = Owner,
            Kind = "Transfer",
            Reference = "REF",
            AccountId = Guid.NewGuid(),
            RecipientAccountId = Guid.NewGuid(),
            Amount = 10,
            Fee = 0,
            Currency = "ZAR",
            TargetAmount = 10,
            TargetCurrency = "ZAR",
            QuoteId = null
        };
        var json = JsonSerializer.Serialize(journal, DomainEvent.Json);
        Assert.DoesNotContain("settingsVersion", json);
        Assert.Contains(
            "settingsVersion",
            JsonSerializer.Serialize(
                new global::DisputePortal.Platform.Contracts.PostJournalRequest(journal) { SettingsVersion = 2 },
                DomainEvent.Json));
    }

    [Theory]
    [InlineData("USD", "ZA", "ZAR", "ZA", "country_currency_unavailable")]
    [InlineData("ZAR", "ZW", "ZAR", "ZA", "country_currency_unavailable")]
    [InlineData("ZAR", "ZA", "USD", "ZA", "recipient_unavailable")]
    [InlineData("ZAR", "ZA", "ZAR", "ZW", "recipient_unavailable")]
    public void New_transfers_only_use_the_saved_jurisdiction_and_default_currency(
        string sourceCurrency,
        string sourceCountry,
        string targetCurrency,
        string targetCountry,
        string code)
    {
        var source = new global::DisputePortal.Platform.Contracts.AccountRecord(Account())
        {
            Currency = sourceCurrency,
            CountryCode = sourceCountry
        };
        var target = new global::DisputePortal.Platform.Contracts.AccountRecord(Account())
        {
            Currency = targetCurrency,
            CountryCode = targetCountry
        };
        var error = Assert.Throws<PlatformException>(() => PaymentPolicy.ValidatePaymentPolicy(
            new("Transfer", source.Id, target.Id, 10),
            Owner,
            source,
            target,
            Policy()));
        Assert.Equal(code, error.Code);
    }

    [Theory]
    [InlineData("Blocked", "Active")]
    [InlineData("Closed", "Active")]
    [InlineData("Active", "Blocked")]
    [InlineData("Active", "Closed")]
    public void A_nonactive_source_or_destination_cannot_transfer(string sourceStatus, string targetStatus)
    {
        var source = new global::DisputePortal.Platform.Contracts.AccountRecord(Account())
        {
            Status = sourceStatus
        };
        var target = new global::DisputePortal.Platform.Contracts.AccountRecord(Account())
        {
            Status = targetStatus
        };
        Assert.Throws<PlatformException>(() => PaymentPolicy.ValidatePaymentPolicy(
            new("Transfer", source.Id, target.Id, 10),
            Owner,
            source,
            target,
            Policy()));
    }

    [Theory]
    [InlineData("ConversionId")]
    [InlineData("Amount")]
    [InlineData("Currency")]
    public void A_historical_conversion_snapshot_cannot_authorize_a_new_payment(string mismatch)
    {
        var source = Account();
        var target = Account();
        var request = new PaymentRequest(Guid.NewGuid(), "Transfer", source.Id, target.Id, 10, QuoteId: Guid.NewGuid());
        var quote = new PaymentQuote
        {
            Id = request.QuoteId!.Value,
            UserId = Owner,
            Kind = "Transfer",
            AccountId = source.Id,
            RecipientAccountId = target.Id,
            Amount = 10,
            Currency = "ZAR",
            Fee = 0,
            TargetAmount = 10,
            TargetCurrency = "ZAR",
            FxQuoteId = null,
            SettingsVersion = 1,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5)
        };
        quote = mismatch switch
        {
            "ConversionId" => new global::DisputePortal.Platform.Contracts.PaymentQuote(quote)
            {
                FxQuoteId = Guid.NewGuid()
            },
            "Amount" => new global::DisputePortal.Platform.Contracts.PaymentQuote(quote)
            {
                TargetAmount = 11
            },
            _ => new global::DisputePortal.Platform.Contracts.PaymentQuote(quote)
            {
                TargetCurrency = "USD"
            }
        };
        var error = Assert.Throws<PlatformException>(() => PaymentPolicy.Match(request, quote, Owner));
        Assert.Equal("cross_currency_transfer_unsupported", error.Code);
    }
}
