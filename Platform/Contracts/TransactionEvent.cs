namespace DisputePortal.Platform.Contracts;

public class TransactionEvent
{
    public Guid TransactionId { get; set; }
    public Guid PaymentId { get; set; }
    public Guid UserId { get; set; }
    public string Kind { get; set; } = null!;
    public string Reference { get; set; } = null!;
    public Guid AccountId { get; set; }
    public Guid? RecipientAccountId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = null!;
    public decimal? TargetAmount { get; set; }
    public string? TargetCurrency { get; set; }
    public decimal Fee { get; set; }
    public string Status { get; set; } = null!;
    public string? FailureCode { get; set; } = null;

    public TransactionEvent()
    {
    }

    public TransactionEvent(TransactionEvent source)
    {
        TransactionId = source.TransactionId;
        PaymentId = source.PaymentId;
        UserId = source.UserId;
        Kind = source.Kind;
        Reference = source.Reference;
        AccountId = source.AccountId;
        RecipientAccountId = source.RecipientAccountId;
        Amount = source.Amount;
        Currency = source.Currency;
        TargetAmount = source.TargetAmount;
        TargetCurrency = source.TargetCurrency;
        Fee = source.Fee;
        Status = source.Status;
        FailureCode = source.FailureCode;
    }
}
