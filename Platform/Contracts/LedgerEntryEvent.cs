namespace DisputePortal.Platform.Contracts;

public class LedgerEntryEvent
{
    public Guid JournalId { get; set; }
    public Guid EntryId { get; set; }
    public Guid AccountId { get; set; }
    public string Currency { get; set; } = null!;
    public string Side { get; set; } = null!;
    public decimal Amount { get; set; }
}
