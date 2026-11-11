using DisputePortal.Api.Models.Domain;
using Xunit;

namespace DisputePortal.UnitTests;

public sealed class CaseDeadlineRulesTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-31T10:00:00Z");

    [Fact]
    public void Future_response_and_resolution_are_allowed() => Assert.Null(CaseDeadlineRules.ValidateCaseDeadlines(
        Now.AddDays(-1),
        null,
        null,
        Now.AddHours(1),
        Now.AddDays(2),
        Now));

    [Theory]
    [InlineData(-1, 24)]
    [InlineData(48, 24)]
    [InlineData(1, -1)]
    [InlineData(1, 9000)]
    public void Past_reversed_or_unbounded_targets_are_rejected(int responseHours, int dueHours) =>
        Assert.NotNull(CaseDeadlineRules.ValidateCaseDeadlines(
        Now.AddDays(-1),
        null,
        null,
        Now.AddHours(responseHours),
        Now.AddHours(dueHours),
        Now));

    [Fact]
    public void A_recorded_first_response_locks_its_original_target()
    {
        var target = Now.AddHours(-1);
        Assert.Null(CaseDeadlineRules.ValidateCaseDeadlines(
            Now.AddDays(-1),
            Now.AddHours(-2),
            Now.AddDays(4),
            Now.AddDays(4),
            Now.AddDays(1),
            Now));
        Assert.Null(CaseDeadlineRules.ValidateCaseDeadlines(Now.AddDays(-1), Now.AddHours(-2), target, target, Now.AddDays(2), Now));
        Assert.NotNull(CaseDeadlineRules.ValidateCaseDeadlines(
            Now.AddDays(-1),
            Now.AddHours(-2),
            target,
            Now.AddHours(1),
            Now.AddDays(2),
            Now));
    }
}
