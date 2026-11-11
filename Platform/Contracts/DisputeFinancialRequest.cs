namespace DisputePortal.Platform.Contracts;

public class DisputeFinancialRequest
{
    public Guid TransactionId { get; set; }
    public Guid RequestId { get; set; }
    public Guid DisputeId { get; set; }
    public Guid OriginalTransactionId { get; set; }
    public Guid UserId { get; set; }
    public Guid AccountId { get; set; }
    public string Kind { get; set; } = null!;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = null!;
    public string Reference { get; set; } = null!;
    public Guid? ActorId { get; set; }

    public DisputeFinancialRequest()
    {
    }

    public DisputeFinancialRequest(DisputeFinancialRequest source)
    {
        TransactionId = source.TransactionId;
        RequestId = source.RequestId;
        DisputeId = source.DisputeId;
        OriginalTransactionId = source.OriginalTransactionId;
        UserId = source.UserId;
        AccountId = source.AccountId;
        Kind = source.Kind;
        Amount = source.Amount;
        Currency = source.Currency;
        Reference = source.Reference;
        ActorId = source.ActorId;
    }
}
