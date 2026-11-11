namespace DisputePortal.Platform.Ledger.Domain;

public class PostingLine
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public string Currency { get; set; } = null!;
    public string Side { get; set; } = null!;
    public decimal Amount { get; set; }
}
