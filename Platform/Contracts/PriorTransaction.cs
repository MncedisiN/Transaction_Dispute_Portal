namespace DisputePortal.Platform.Contracts;

public class PriorTransaction
{
    public Guid Id { get; set; }
    public string Reference { get; set; } = null!;
    public DateTimeOffset OccurredAt { get; set; }
    public string Description { get; set; } = null!;
    public string MerchantName { get; set; } = null!;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = null!;
    public string Type { get; set; } = null!;
    public string Direction { get; set; } = null!;
    public string Status { get; set; } = null!;
    public bool DisputeEligible { get; set; }
    public decimal CurrentDelta { get; set; }
    public decimal ReservedDelta { get; set; }
    public int Unknown { get; set; }
    public Guid? OriginalTransactionId { get; set; } = null;
}
