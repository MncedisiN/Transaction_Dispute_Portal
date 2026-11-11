using DisputePortal.Platform.Contracts;
using Xunit;

namespace DisputePortal.Platform.Tests;

public sealed class TransactionIdsTests
{
    private static Guid Id(int number) => new(number, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1);

    [Fact]
    public void Detects_duplicates_among_one_million_identifiers_in_one_pass()
    {
        var ids = Enumerable.Range(0, 1_000_000).Select(Id).Concat([Id(123), Id(999_999), Id(123)]);
        Assert.Equal(new[] { Id(123), Id(999_999) }, TransactionIds.FindDuplicates(ids));
    }

    [Fact]
    public void Empty_batch_has_no_duplicates() => Assert.Empty(TransactionIds.FindDuplicates([]));

    [Fact]
    public void Distinct_identifiers_are_not_duplicates() => Assert.Empty(TransactionIds.FindDuplicates([Id(1), Id(2)]));

    [Fact]
    public void Returns_each_duplicate_only_once() => Assert.Equal(new[] { Id(1) }, TransactionIds.FindDuplicates([Id(1), Id(1), Id(1)]));
}
