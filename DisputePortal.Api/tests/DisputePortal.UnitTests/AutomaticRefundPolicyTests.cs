using DisputePortal.Api.Models.Domain;
using Xunit;

namespace DisputePortal.UnitTests;

public sealed class AutomaticRefundPolicyTests
{
    private static AutomaticRefundPolicy Policy() => new global::DisputePortal.Api.Models.Domain.AutomaticRefundPolicy()
    {
        Version = 1,
        DuplicateWindowMinutes = 10,
        MaximumAmount = 250m,
        MaximumAgeHours = 48,
        DailyLimit = 500m
    };

    [Fact]
    public void Duplicate_window_is_policy_data_and_account_currency_is_preserved()
    {
        var policy = new AutomaticRefundPolicy(Policy()) { Enabled = true, DuplicateWindowMinutes = 1 };
        Assert.False(policy.Approves(Valid(), Now));
        policy.DuplicateWindowMinutes = 3;
        Assert.True(policy.Approves(Valid(), Now));
        var facts = new AutomaticRefundFacts(Valid()) { Currency = "ZWG", AccountCurrency = "ZWG" };
        Assert.True(policy.Approves(facts, Now));
        facts.AccountCurrency = "ZAR";
        Assert.False(policy.Approves(facts, Now));
    }

    [Fact]
    public void Unconfigured_policy_cannot_approve_a_refund() => Assert.False(new AutomaticRefundPolicy { Enabled = true }.IsValid());

    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-31T10:00:00Z");

    private static AutomaticRefundFacts Valid() => new global::DisputePortal.Api.Models.Domain.AutomaticRefundFacts
    {
        Amount = 79.90m,
        Currency = "ZAR",
        AccountCurrency = "ZAR",
        Type = "CardPurchase",
        Direction = "Debit",
        Status = "Posted",
        TransactionAt = Now.AddMinutes(-2),
        OriginalAt = Now.AddMinutes(-4),
        Active = true,
        ContactVerified = true,
        TrustedLocalProof = true,
        MatchesOriginal = true,
        OriginalDisputed = false,
        FraudOpen = false,
        AlreadyRefunded = false,
        RefundedToday = 0
    };

    [Fact]
    public void Only_verified_trusted_low_risk_duplicates_pass()
    {
        var policy = new global::DisputePortal.Api.Models.Domain.AutomaticRefundPolicy(Policy())
        {
            Enabled = true
        };
        Assert.True(policy.Approves(Valid(), Now));
        Assert.False(policy.Approves(
            new global::DisputePortal.Api.Models.Domain.AutomaticRefundFacts(Valid()) { TrustedLocalProof = false },
            Now));
        Assert.False(policy.Approves(
            new global::DisputePortal.Api.Models.Domain.AutomaticRefundFacts(Valid()) { OriginalDisputed = true },
            Now));
        Assert.False(policy.Approves(new global::DisputePortal.Api.Models.Domain.AutomaticRefundFacts(Valid()) { FraudOpen = true }, Now));
        Assert.False(policy.Approves(
            new global::DisputePortal.Api.Models.Domain.AutomaticRefundFacts(Valid()) { MatchesOriginal = false },
            Now));
        Assert.False(policy.Approves(
            new global::DisputePortal.Api.Models.Domain.AutomaticRefundFacts(Valid()) { AlreadyRefunded = true },
            Now));
        Assert.False(policy.Approves(
            new global::DisputePortal.Api.Models.Domain.AutomaticRefundFacts(Valid()) { ContactVerified = false },
            Now));
    }

    [Fact]
    public void Limits_currency_age_and_status_are_enforced()
    {
        var policy = new global::DisputePortal.Api.Models.Domain.AutomaticRefundPolicy(Policy())
        {
            Enabled = true
        };
        Assert.False(policy.Approves(new global::DisputePortal.Api.Models.Domain.AutomaticRefundFacts(Valid()) { Amount = 251 }, Now));
        Assert.False(policy.Approves(new global::DisputePortal.Api.Models.Domain.AutomaticRefundFacts(Valid()) { RefundedToday = 450 }, Now));
        Assert.False(policy.Approves(new global::DisputePortal.Api.Models.Domain.AutomaticRefundFacts(Valid()) { Currency = "USD" }, Now));
        Assert.False(policy.Approves(new global::DisputePortal.Api.Models.Domain.AutomaticRefundFacts(Valid()) { Status = "Reversed" }, Now));
        Assert.False(policy.Approves(
            new global::DisputePortal.Api.Models.Domain.AutomaticRefundFacts(Valid()) { Direction = "Credit" },
            Now));
        Assert.False(policy.Approves(
            new global::DisputePortal.Api.Models.Domain.AutomaticRefundFacts(Valid()) { TransactionAt = Now.AddSeconds(1) },
            Now));
        Assert.False(policy.Approves(
            new global::DisputePortal.Api.Models.Domain.AutomaticRefundFacts(Valid()) { OriginalAt = Now.AddMinutes(-15) },
            Now));
        Assert.False(policy.Approves(new global::DisputePortal.Api.Models.Domain.AutomaticRefundFacts(Valid()) { Amount = -1 }, Now));
    }

    [Fact]
    public void Policies_have_bounded_precision_and_versions()
    {
        Assert.True((new global::DisputePortal.Api.Models.Domain.AutomaticRefundPolicy(Policy())
        {
            MaximumAmount = 2500m,
            DailyLimit = 10_000m,
            MaximumAgeHours = 720
        }).IsValid());
        Assert.False((new global::DisputePortal.Api.Models.Domain.AutomaticRefundPolicy(Policy())
        {
            MaximumAmount = DisputePolicyLimits.MaximumMoney + 1m,
            DailyLimit = DisputePolicyLimits.MaximumMoney + 1m
        }).IsValid());
        Assert.False((new global::DisputePortal.Api.Models.Domain.AutomaticRefundPolicy(Policy()) { MaximumAmount = 10.001m }).IsValid());
        Assert.False((new global::DisputePortal.Api.Models.Domain.AutomaticRefundPolicy(Policy()) { Version = 0 }).IsValid());
        Assert.False((new global::DisputePortal.Api.Models.Domain.AutomaticRefundPolicy(Policy()) { MaximumAgeHours = DisputePolicyLimits.MaximumHours + 1 }).IsValid());
    }

    [Fact]
    public void Credit_card_merchant_purchases_require_the_same_verified_duplicate_proof()
    {
        var policy = new global::DisputePortal.Api.Models.Domain.AutomaticRefundPolicy(Policy())
        {
            Enabled = true
        };
        var facts = new global::DisputePortal.Api.Models.Domain.AutomaticRefundFacts(Valid())
        {
            Type = "CreditCardPayment"
        };
        Assert.True(policy.Approves(facts, Now));
        Assert.False(policy.Approves(
            new global::DisputePortal.Api.Models.Domain.AutomaticRefundFacts(facts) { TrustedLocalProof = false },
            Now));
        Assert.False(policy.Approves(
            new global::DisputePortal.Api.Models.Domain.AutomaticRefundFacts(facts) { MatchesOriginal = false },
            Now));
    }

    [Theory]
    [InlineData("EFT", "Unknown", true)]
    [InlineData("EFT", "Disputed", true)]
    [InlineData("RM", "Unknown", true)]
    [InlineData("DebiCheck", "Valid", false)]
    [InlineData("DebiCheck", "Disputed", false)]
    [InlineData("EFT", "Valid", false)]
    [InlineData(null, "Unknown", false)]
    public void Debit_order_policy_checks_scheme_and_mandate(string? scheme, string? mandate, bool expected)
    {
        var policy = new global::DisputePortal.Api.Models.Domain.AutomaticRefundPolicy(Policy())
        {
            Enabled = true
        };
        var facts = new global::DisputePortal.Api.Models.Domain.AutomaticRefundFacts(Valid())
        {
            Type = "DebitOrder",
            MatchesOriginal = false,
            OriginalAt = DateTimeOffset.MinValue
        };
        Assert.Equal(expected, policy.ApprovesDebitOrder(facts, scheme, mandate, Now));
    }

    [Fact]
    public void Debit_order_policy_enforces_database_limits_proof_and_financial_guards()
    {
        var policy = new global::DisputePortal.Api.Models.Domain.AutomaticRefundPolicy(Policy())
        {
            Enabled = true
        };
        var facts = new global::DisputePortal.Api.Models.Domain.AutomaticRefundFacts(Valid())
        {
            Type = "DebitOrder"
        };
        Assert.True(policy.ApprovesDebitOrder(facts, "EFT", "Unknown", Now));
        foreach (var denied in new[]
        {
            new global::DisputePortal.Api.Models.Domain.AutomaticRefundFacts(facts)
            {
                Amount = 251
            },
            new global::DisputePortal.Api.Models.Domain.AutomaticRefundFacts(facts)
            {
                RefundedToday = 499
            },
            new global::DisputePortal.Api.Models.Domain.AutomaticRefundFacts(facts)
            {
                TransactionAt = Now.AddHours(-49)
            },
            new global::DisputePortal.Api.Models.Domain.AutomaticRefundFacts(facts)
            {
                TransactionAt = Now.AddSeconds(1)
            },
            new global::DisputePortal.Api.Models.Domain.AutomaticRefundFacts(facts)
            {
                TrustedLocalProof = false
            },
            new global::DisputePortal.Api.Models.Domain.AutomaticRefundFacts(facts)
            {
                ContactVerified = false
            },
            new global::DisputePortal.Api.Models.Domain.AutomaticRefundFacts(facts)
            {
                Active = false
            },
            new global::DisputePortal.Api.Models.Domain.AutomaticRefundFacts(facts)
            {
                FraudOpen = true
            },
            new global::DisputePortal.Api.Models.Domain.AutomaticRefundFacts(facts)
            {
                AlreadyRefunded = true
            },
            new global::DisputePortal.Api.Models.Domain.AutomaticRefundFacts(facts)
            {
                Currency = "USD"
            },
            new global::DisputePortal.Api.Models.Domain.AutomaticRefundFacts(facts)
            {
                Direction = "Credit"
            },
            new global::DisputePortal.Api.Models.Domain.AutomaticRefundFacts(facts)
            {
                Status = "Reversed"
            },
            new global::DisputePortal.Api.Models.Domain.AutomaticRefundFacts(facts)
            {
                Type = "CardPurchase"
            }
        }

        )
            Assert.False(policy.ApprovesDebitOrder(denied, "EFT", "Unknown", Now));
        Assert.False((new global::DisputePortal.Api.Models.Domain.AutomaticRefundPolicy(policy) { Enabled = false }).ApprovesDebitOrder(
            facts,
            "EFT",
            "Unknown",
            Now));
        Assert.True((new global::DisputePortal.Api.Models.Domain.AutomaticRefundPolicy(policy)
        {
            MaximumAmount = 50,
            DailyLimit = 100
        }).ApprovesDebitOrder(
            new global::DisputePortal.Api.Models.Domain.AutomaticRefundFacts(facts) { Amount = 49 },
            "EFT",
            "Unknown",
            Now));
        Assert.False((new global::DisputePortal.Api.Models.Domain.AutomaticRefundPolicy(policy)
        {
            MaximumAmount = 50,
            DailyLimit = 100
        }).ApprovesDebitOrder(facts, "EFT", "Unknown", Now));
    }
}
