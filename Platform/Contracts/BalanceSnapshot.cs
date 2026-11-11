namespace DisputePortal.Platform.Contracts;

public class BalanceSnapshot
{
    public Guid AccountId { get; set; }
    public Guid UserId { get; set; }
    public string Currency { get; set; } = null!;
    public decimal LedgerBalance { get; set; }
    public decimal ReservedBalance { get; set; }
    public long Version { get; set; }
}
