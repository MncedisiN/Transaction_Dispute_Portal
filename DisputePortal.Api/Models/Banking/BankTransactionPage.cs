namespace DisputePortal.Api.Models.Banking;

public class BankTransactionPage
{
    public string ExternalAccountId { get; set; } = null!;
    public IReadOnlyList<BankTransaction> Items { get; set; } = null!;
    public string? NextCursor { get; set; }
    public bool HasMore { get; set; }

    public BankTransactionPage()
    {
    }

    public BankTransactionPage(BankTransactionPage source)
    {
        ExternalAccountId = source.ExternalAccountId;
        Items = source.Items;
        NextCursor = source.NextCursor;
        HasMore = source.HasMore;
    }
}
