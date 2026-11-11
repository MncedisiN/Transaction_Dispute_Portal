using DisputePortal.Api.Models.Domain;
using Xunit;

namespace DisputePortal.UnitTests;

public sealed class DisputeWorkflowTests
{
    [Theory]
    [InlineData(DisputeStatus.Submitted, DisputeStatus.UnderReview)]
    [InlineData(DisputeStatus.Submitted, DisputeStatus.Withdrawn)]
    [InlineData(DisputeStatus.UnderReview, DisputeStatus.AwaitingCustomer)]
    [InlineData(DisputeStatus.UnderReview, DisputeStatus.Resolved)]
    [InlineData(DisputeStatus.UnderReview, DisputeStatus.Declined)]
    [InlineData(DisputeStatus.AwaitingCustomer, DisputeStatus.UnderReview)]
    [InlineData(DisputeStatus.Resolved, DisputeStatus.UnderReview)]
    [InlineData(DisputeStatus.Declined, DisputeStatus.UnderReview)]
    [InlineData(DisputeStatus.Withdrawn, DisputeStatus.UnderReview)]
    public void Allows_documented_transitions(DisputeStatus from, DisputeStatus to) =>
        Assert.True(DisputeWorkflow.CanTransition(from, to));

    [Theory]
    [InlineData(DisputeStatus.Resolved, DisputeStatus.Submitted)]
    [InlineData(DisputeStatus.Declined, DisputeStatus.Resolved)]
    [InlineData(DisputeStatus.Withdrawn, DisputeStatus.Submitted)]
    [InlineData(DisputeStatus.Submitted, DisputeStatus.Resolved)]
    public void Rejects_terminal_and_skipped_transitions(DisputeStatus from, DisputeStatus to) =>
        Assert.False(DisputeWorkflow.CanTransition(from, to));

    [Theory]
    [InlineData(DisputeStatus.Submitted, true)]
    [InlineData(DisputeStatus.UnderReview, true)]
    [InlineData(DisputeStatus.AwaitingCustomer, true)]
    [InlineData(DisputeStatus.Resolved, false)]
    [InlineData(DisputeStatus.Declined, false)]
    [InlineData(DisputeStatus.Withdrawn, false)]
    public void Identifies_active_cases(DisputeStatus status, bool expected) => Assert.Equal(expected, DisputeWorkflow.IsActive(status));

    [Theory]
    [InlineData(DisputeStatus.Submitted, true)]
    [InlineData(DisputeStatus.UnderReview, true)]
    [InlineData(DisputeStatus.AwaitingCustomer, true)]
    [InlineData(DisputeStatus.Resolved, false)]
    [InlineData(DisputeStatus.Declined, false)]
    [InlineData(DisputeStatus.Withdrawn, false)]
    public void Customer_can_stop_only_an_active_case(DisputeStatus status, bool expected) => Assert.Equal(
        expected,
        DisputeWorkflow.CanCustomerWithdraw(status));

    [Theory]
    [InlineData("Submitted", true)]
    [InlineData("UnderReview", true)]
    [InlineData("AwaitingCustomer", true)]
    [InlineData("Resolved", false)]
    [InlineData("Declined", false)]
    [InlineData("Withdrawn", false)]
    [InlineData("Closed", false)]
    [InlineData("0", false)]
    [InlineData("Unknown", false)]
    [InlineData(null, false)]
    public void Case_messaging_requires_a_known_active_status(string? status, bool expected) => Assert.Equal(
        expected,
        DisputeWorkflow.CanSendCaseMessage(status));
}
