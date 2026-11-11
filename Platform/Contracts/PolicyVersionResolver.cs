namespace DisputePortal.Platform.Contracts;

public static class PolicyVersionResolver
{
    // A reached publication supersedes ALL older revisions of its identity, even
    // when deleted/expired/disabled. No implicit resurrection or baseline fallback.
    // Across scoped identities: priority DESC, policy public ID ordinal ASC.
    public static PolicyResolution<T> ResolvePublishedPolicyVersion<T>(
        IEnumerable<T> source,
        string scope,
        DateTimeOffset evaluationTimeUtc)
        where T : PolicyVersionMetadata
    {
        var at = evaluationTimeUtc.ToUniversalTime();
        var all = source.ToArray();
        var rows = all.Where(x => x.Scope == scope && x.PublishedAtUtc is not null).ToArray();
        var heads = rows.Where(x => x.EffectiveFromUtc <= at).GroupBy(x => x.PolicyPublicId).Select(g => g.OrderByDescending(x => x.Version).First()).ToArray();
        var selected = heads.Where(x => !x.IsDeleted && (x.EffectiveToUtc is null || at < x.EffectiveToUtc)).OrderByDescending(x => x.Priority).ThenBy(
            x => x.PolicyPublicId.ToString("D"),
            StringComparer.Ordinal).FirstOrDefault();
        var changes = rows.SelectMany(x => new DateTimeOffset?[] { x.EffectiveFromUtc, x.EffectiveToUtc }).Where(x => x > at).Select(x => x!.Value).ToArray();
        var next = changes.Length == 0 ? (DateTimeOffset?)null : changes.Min();
        foreach (var row in all)
        {
            row.EvaluationTimeUtc = at;
            row.NextChangeAtUtc = next;
            row.State = row.IsDeleted ? "Deleted" : row.PublishedAtUtc is null ? "Unpublished" : heads.Any(x => x.PolicyPublicId == row.PolicyPublicId && x.Version > row.Version) ? "Superseded" : row.EffectiveFromUtc > at ? "Scheduled" : row.EffectiveToUtc <= at ? "Expired" : "Effective";
        }

        var result = new PolicyResolution<T>
        {
            Selected = selected,
            EvaluationTimeUtc = at,
            NextChangeAtUtc = next,
            SelectionReason = selected is null ? "NoApplicablePolicy" : "LatestReachedVersionThenPriorityAndPolicyId"
        };
        if (selected is not null)
        {
            selected.SelectionReason = result.SelectionReason;
        }

        return result;
    }
}
