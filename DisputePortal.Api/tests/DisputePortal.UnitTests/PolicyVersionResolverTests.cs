using DisputePortal.Platform.Contracts;
using Xunit;

namespace DisputePortal.UnitTests;

public class PolicyVersionResolverTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Policy = Guid.Parse("10000000-0000-0000-0000-000000000001");

    private static PolicyVersionMetadata Version(
        int version,
        DateTimeOffset from,
        DateTimeOffset? to = null,
        bool deleted = false,
        int priority = 0,
        string scope = "Global") => new()
        {
            PolicyPublicId = Policy,
            VersionPublicId = Guid.NewGuid(),
            Version = version,
            EffectiveFromUtc = from,
            EffectiveToUtc = to,
            PublishedAtUtc = At.AddDays(-10),
            IsDeleted = deleted,
            Priority = priority,
            Scope = scope
        };

    [Fact]
    public void Starts_include_and_ends_exclude_the_exact_evaluation_instant()
    {
        var row = Version(6, At, At.AddHours(1));
        Assert.Same(row, PolicyVersionResolver.ResolvePublishedPolicyVersion([row], "Global", At).Selected);
        Assert.Null(PolicyVersionResolver.ResolvePublishedPolicyVersion([row], "Global", At.AddHours(1)).Selected);
    }

    [Fact]
    public void Future_versions_do_not_apply_early_and_report_the_next_boundary()
    {
        var old = Version(6, At.AddDays(-1));
        var next = Version(7, At.AddHours(1));
        var result = PolicyVersionResolver.ResolvePublishedPolicyVersion([old, next], "Global", At);
        Assert.Same(old, result.Selected);
        Assert.Equal(next.EffectiveFromUtc, result.NextChangeAtUtc);
        Assert.Equal("Scheduled", next.State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Expired_or_deleted_replacement_does_not_resurrect_an_older_revision(bool deleted)
    {
        var old = Version(6, At.AddDays(-3));
        var next = Version(7, At.AddDays(-2), deleted ? null : At.AddDays(-1), deleted);
        Assert.Null(PolicyVersionResolver.ResolvePublishedPolicyVersion([old, next], "Global", At).Selected);
        Assert.Equal("Superseded", old.State);
    }

    [Fact]
    public void Scope_precedes_priority_and_ties_are_deterministic()
    {
        var a = Version(1, At.AddDays(-1), priority: 2);
        var b = Version(1, At.AddDays(-1), priority: 2);
        b.PolicyPublicId = Guid.Parse("20000000-0000-0000-0000-000000000001");
        var foreign = Version(99, At.AddDays(-1), priority: 999, scope: "Other");
        Assert.Same(a, PolicyVersionResolver.ResolvePublishedPolicyVersion([b, foreign, a], "Global", At).Selected);
        b.Priority = 3;
        Assert.Same(b, PolicyVersionResolver.ResolvePublishedPolicyVersion([a, b], "Global", At).Selected);
    }

    [Fact]
    public void Unpublished_presets_are_not_eligible_even_when_their_version_is_higher()
    {
        var old = Version(6, At.AddDays(-1));
        var preset = Version(99, At.AddDays(-1));
        preset.PublishedAtUtc = null;
        Assert.Same(old, PolicyVersionResolver.ResolvePublishedPolicyVersion([old, preset], "Global", At).Selected);
    }
}
