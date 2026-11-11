namespace DisputePortal.Platform.Contracts;

public class LedgerPostedEvent
{
    public Guid JournalId { get; set; }
    public Guid TransactionId { get; set; }
    public string Kind { get; set; } = null!;
    public BalanceSnapshot[] Balances { get; set; } = null!;
}
