using DisputePortal.Api.Models.Domain;
using Xunit;

namespace DisputePortal.UnitTests;

public sealed class DisputeRiskPolicyTests
{
    private static DisputeRiskPolicy Policy() => new global::DisputePortal.Api.Models.Domain.DisputeRiskPolicy
    {
        Version = 1,
        HighValueThreshold = 5000m,
        ExtraCheckAfterDays = 30,
        DebitOrderWindowDays = 60,
        DebitOrderAppLimit = 800m
    };

    [Theory]
    [InlineData(799.99, false)]
    [InlineData(800, true)]
    [InlineData(5000, true)]
    public void Debit_order_amount_boundary_requires_verification(decimal amount, bool required)
    {
        var now = DateTimeOffset.Parse("2026-10-30T10:00:00Z");
        var result = Policy().AssessTransactionDisputeRisk(
            amount,
            "ZAR",
            "DebitOrder",
            now.AddDays(-2),
            now,
            TimeZoneInfo.FindSystemTimeZoneById("Africa/Johannesburg"));
        Assert.True(result.Allowed);
        Assert.Equal(required, result.RequiresVerification);
    }

    [Theory]
    [InlineData("2026-10-30T21:59:59Z", true)]
    [InlineData("2026-10-30T22:00:00Z", false)]
    public void Sixtieth_day_ends_at_South_African_midnight(string now, bool allowed)
    {
        var result = Policy().AssessTransactionDisputeRisk(
            100,
            "ZAR",
            "DebitOrder",
            DateTimeOffset.Parse("2026-08-30T22:30:00Z"),
            DateTimeOffset.Parse(now),
            TimeZoneInfo.FindSystemTimeZoneById("Africa/Johannesburg"));
        Assert.Equal(allowed, result.Allowed);
    }

    [Fact]
    public void Authorised_DebiCheck_is_referred_without_promising_reversal()
    {
        var now = DateTimeOffset.UtcNow;
        var result = Policy().AssessTransactionDisputeRisk(
            100,
            "ZAR",
            "DebitOrder",
            now.AddDays(-1),
            now,
            TimeZoneInfo.FindSystemTimeZoneById("Africa/Johannesburg"),
            "DebiCheck",
            "Valid");
        Assert.False(result.Allowed);
        Assert.True(result.AssistedReview);
    }

    [Fact]
    public void Card_age_is_an_extra_check_not_a_debit_order_deadline()
    {
        var now = DateTimeOffset.UtcNow;
        var result = Policy().AssessTransactionDisputeRisk(
            10,
            "ZAR",
            "CardPurchase",
            now.AddDays(-90),
            now,
            TimeZoneInfo.FindSystemTimeZoneById("Africa/Johannesburg"));
        Assert.True(result.Allowed);
        Assert.True(result.RequiresVerification);
    }

    [Fact]
    public void Foreign_currency_requires_review_without_fake_conversion()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.True(Policy().AssessTransactionDisputeRisk(
            10,
            "USD",
            "CardPurchase",
            now,
            now,
            TimeZoneInfo.FindSystemTimeZoneById("Africa/Johannesburg")).RequiresVerification);
    }

    [Fact]
    public void Operational_values_can_exceed_the_old_amount_and_age_caps() => new DisputeRiskPolicy
    {
        Version = 1,
        HighValueThreshold = 2_000_000m,
        ExtraCheckAfterDays = 365,
        DebitOrderWindowDays = 365,
        DebitOrderAppLimit = 25_000m
    }.ValidateDisputeRiskPolicy();

    [Theory]
    [InlineData(36501, 800)]
    [InlineData(60, 0)]
    [InlineData(0, 800)]
    [InlineData(60, 800.001)]
    public void Policy_retains_technical_bounds_and_cent_precision(int days, decimal amount) =>
        Assert.Throws<ArgumentException>(() => new DisputeRiskPolicy
    {
        Version = 1,
        HighValueThreshold = 5000m,
        ExtraCheckAfterDays = 30,
        DebitOrderWindowDays = days,
        DebitOrderAppLimit = amount
    }.ValidateDisputeRiskPolicy());
}
