namespace DisputePortal.Platform.Contracts;

public class PaymentRecord
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid RequestId { get; set; }
    public string Kind { get; set; } = null!;
    public Guid AccountId { get; set; }
    public Guid? RecipientAccountId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = null!;
    public decimal Fee { get; set; }
    public decimal? TargetAmount { get; set; }
    public string? TargetCurrency { get; set; }
    public string Status { get; set; } = null!;
    public string Reference { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public int SettingsVersion { get; set; } = 0;
    public Guid? FxQuoteId { get; set; } = null;
    public string? FailureCode { get; set; } = null;
    public BalanceSnapshot[]? Balances { get; set; } = null;
    public DateTimeOffset? QuoteExpiresAt { get; set; } = null;

    public PaymentRecord()
    {
    }

    public PaymentRecord(PaymentRecord source)
    {
        Id = source.Id;
        UserId = source.UserId;
        RequestId = source.RequestId;
        Kind = source.Kind;
        AccountId = source.AccountId;
        RecipientAccountId = source.RecipientAccountId;
        Amount = source.Amount;
        Currency = source.Currency;
        Fee = source.Fee;
        TargetAmount = source.TargetAmount;
        TargetCurrency = source.TargetCurrency;
        Status = source.Status;
        Reference = source.Reference;
        CreatedAt = source.CreatedAt;
        SettingsVersion = source.SettingsVersion;
        FxQuoteId = source.FxQuoteId;
        FailureCode = source.FailureCode;
        Balances = source.Balances;
        QuoteExpiresAt = source.QuoteExpiresAt;
    }
}
