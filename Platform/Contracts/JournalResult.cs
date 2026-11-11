namespace DisputePortal.Platform.Contracts;

public class JournalResult
{
    public Guid JournalId { get; set; }
    public Guid TransactionId { get; set; }
    public string Reference { get; set; } = "";
    public BalanceSnapshot[] Balances { get; set; } = [];
    public bool Replayed { get; set; } = false;

    public JournalResult()
    {
    }

    public JournalResult(Guid JournalId, Guid TransactionId, string Reference, BalanceSnapshot[] Balances, bool Replayed = false)
    {
        this.JournalId = JournalId;
        this.TransactionId = TransactionId;
        this.Reference = Reference;
        this.Balances = Balances;
        this.Replayed = Replayed;
    }
}
