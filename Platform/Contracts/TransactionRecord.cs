namespace DisputePortal.Platform.Contracts;

public class TransactionRecord
{
    public Guid Id { get; set; }
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
    public string? FailureCode { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string Direction { get; set; } = "Debit";
    public string? Description { get; set; } = null;

    public TransactionRecord()
    {
    }

    public TransactionRecord(TransactionRecord source)
    {
        Id = source.Id;
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
        CreatedAt = source.CreatedAt;
        UpdatedAt = source.UpdatedAt;
        Direction = source.Direction;
        Description = source.Description;
    }
}
