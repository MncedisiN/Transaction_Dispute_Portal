using DisputePortal.Api.Models.Domain;
using Xunit;

namespace DisputePortal.UnitTests;

public sealed class CaseRoutingTests
{
    [Theory]
    [InlineData("Duplicate", "CardPurchase", "Card")]
    [InlineData("DebitOrder", "DebitOrder", "DebitOrder")]
    [InlineData("IncorrectAmount", "Transfer", "Payments")]
    [InlineData("Other", "CashWithdrawal", "Payments")]
    [InlineData("Other", "Fee", "General")]
    [InlineData("Unrecognized", "DebitOrder", "Fraud")]
    public void Routing_matches_transaction_and_concern(string concern, string type, string queue) => Assert.Equal(
        queue,
        CaseServicePolicy.Route(concern, type));

    [Theory]
    [InlineData("Unrecognized", "CardPurchase", 6000, "Critical", 5)]
    [InlineData("Unrecognized", "DebitOrder", 100, "High", 5)]
    [InlineData("Duplicate", "CardPurchase", 6000, "High", 4)]
    [InlineData("Duplicate", "Transfer", 100, "Medium", 3)]
    public void Intake_classifies_severity_and_priority(string reason, string type, int amount, string severity, int priority)
    {
        var classification = new CaseServicePolicy().ClassifyCaseReviewQueue(reason, type, amount, "ZAR");
        Assert.Equal(severity, classification.Severity);
        Assert.Equal(priority, classification.Priority);
    }

    [Fact]
    public void Unknown_teams_and_cross_specialist_access_are_denied()
    {
        Assert.False(CaseServicePolicy.CanWork("card_specialist", "Fraud"));
        Assert.False(CaseServicePolicy.CanWork("dispute_agent", "Unknown"));
        Assert.True(CaseServicePolicy.CanWork("debit_order_specialist", "DebitOrder"));
        Assert.False(CaseServicePolicy.CanWork("audit_reader", "Card"));
    }
}
