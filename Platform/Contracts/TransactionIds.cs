namespace DisputePortal.Platform.Contracts;
/// <summary>Batch-local duplicate detection. Persistent idempotency remains a database responsibility.</summary>
public static class TransactionIds
{
    public static IReadOnlyList<Guid> FindDuplicates(IEnumerable<Guid> transactionIds)
    {
        ArgumentNullException.ThrowIfNull(transactionIds);
        var seen = new HashSet<Guid>();
        var reported = new HashSet<Guid>();
        var duplicates = new List<Guid>();
        foreach (var id in transactionIds)
            if (!seen.Add(id) && reported.Add(id))
            {
                duplicates.Add(id);
            }

        return duplicates;
    }
}
