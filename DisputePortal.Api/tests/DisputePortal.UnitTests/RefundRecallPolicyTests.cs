using DisputePortal.Api.Models.Domain;
using Xunit;

namespace DisputePortal.UnitTests;

public sealed class RefundRecallPolicyTests
{
    [Theory]
    [InlineData(100, 150, 50, true, true)]
    [InlineData(100, 150, 50.01, true, false)]
    [InlineData(100, 1000, 0, false, false)]
    [InlineData(100, 0, 0, true, false)]
    [InlineData(0, 100, 0, true, false)]
    public void Recall_uses_known_available_balance_including_reservations(
        decimal amount,
        decimal current,
        decimal reserved,
        bool known,
        bool expected) => Assert.Equal(expected, RefundRecallPolicy.HasAvailableFunds(amount, current, reserved, known));

    [Theory]
    [InlineData("Resolved", true, false, true)]
    [InlineData("Withdrawn", true, false, true)]
    [InlineData("Declined", true, false, true)]
    [InlineData("UnderReview", true, false, false)]
    [InlineData("Resolved", false, false, false)]
    [InlineData("Resolved", true, true, false)]
    public void Reinstatement_requires_a_closed_recalled_case_without_a_prior_child(
        string status,
        bool recalled,
        bool reinstated,
        bool expected) => Assert.Equal(expected, RefundRecallPolicy.CanReinstate(status, recalled, reinstated));
}
