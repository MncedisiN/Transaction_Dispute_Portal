namespace DisputePortal.Platform.Contracts;

public class AccountHistoryImportResult
{
    public Guid AccountId { get; set; }
    public int Transactions { get; set; }
    public decimal Balance { get; set; }
    public decimal Reserved { get; set; }
    public long Version { get; set; }
    public bool Replayed { get; set; }

    public AccountHistoryImportResult()
    {
    }

    public AccountHistoryImportResult(Guid AccountId, int Transactions, decimal Balance, decimal Reserved, long Version, bool Replayed)
    {
        this.AccountId = AccountId;
        this.Transactions = Transactions;
        this.Balance = Balance;
        this.Reserved = Reserved;
        this.Version = Version;
        this.Replayed = Replayed;
    }
}
