using DisputePortal.Api.Configuration;
using DisputePortal.Api.Models.Domain;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Xunit;

namespace DisputePortal.UnitTests;

public class AssessmentRefundSecurityTests
{
    [Theory]
    [InlineData("Development", true, true)]
    [InlineData("Development", false, false)]
    [InlineData("Production", true, false)]
    [InlineData("Staging", true, false)]
    public void Contact_bypass_requires_development_and_the_existing_assessment_switch(string name, bool enabled, bool expected)
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(name);
        var settings = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Assessment:SkipContactVerification"] = enabled.ToString()
        }).Build();
        Assert.Equal(expected, AssessmentSecurity.AllowsUnverifiedContacts(settings, environment));
        if (name != "Development")
        {
            Assert.Throws<InvalidOperationException>(() => ProductionSecurity.ValidateProductionSecurity(settings, environment));
        }
    }

    [Fact]
    public void Missing_assessment_context_requires_verification()
    {
        Assert.False(AssessmentSecurity.AllowsUnverifiedContacts(null, null));
    }

    [Fact]
    public void Assessment_refunds_skip_only_contact_verification()
    {
        var now = DateTimeOffset.UtcNow;
        var facts = new AutomaticRefundFacts
        {
            Amount = 149, Currency = "ZAR", AccountCurrency = "ZAR", Type = "DebitOrder",
            Direction = "Debit", Status = "Posted", TransactionAt = now.AddMinutes(-1),
            Active = true, ContactVerified = false, TrustedLocalProof = true
        };
        var policy = new AutomaticRefundPolicy
        {
            Version = 1, Enabled = true, MaximumAmount = 250, MaximumAgeHours = 48,
            DailyLimit = 500, DuplicateWindowMinutes = 10
        };
        Assert.False(policy.ApprovesDebitOrder(facts, "EFT", "Unknown", now));
        policy.ContactVerificationRequired = false;
        Assert.True(policy.ApprovesDebitOrder(facts, "EFT", "Unknown", now));
        Assert.False(facts.ContactVerified);
        facts.Amount = 251;
        Assert.False(policy.ApprovesDebitOrder(facts, "EFT", "Unknown", now));
        facts.Amount = 149;
        facts.AlreadyRefunded = true;
        Assert.False(policy.ApprovesDebitOrder(facts, "EFT", "Unknown", now));
        facts.AlreadyRefunded = false;
        facts.FraudOpen = true;
        Assert.False(policy.ApprovesDebitOrder(facts, "EFT", "Unknown", now));
        facts.FraudOpen = false;
        Assert.False(policy.ApprovesDebitOrder(facts, "DebiCheck", "Valid", now));
        facts.Type = "CreditCardPayment";
        facts.MatchesOriginal = false;
        Assert.False(policy.Approves(facts, now));
    }
}
