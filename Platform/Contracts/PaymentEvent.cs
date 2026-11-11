using System.Text.Json.Serialization;

namespace DisputePortal.Platform.Contracts;

public class PaymentEvent
{
    public Guid PaymentId { get; set; }
    public Guid UserId { get; set; }
    public Guid RequestId { get; set; }
    public string Kind { get; set; } = null!;
    public Guid AccountId { get; set; }
    public Guid? RecipientAccountId { get; set; }
    public decimal Amount { get; set; }
    public string State { get; set; } = null!;
    public string Reference { get; set; } = null!;
    public string Currency { get; set; } = "";
    public decimal Fee { get; set; } = 0;
    public decimal? TargetAmount { get; set; } = null;
    public string? TargetCurrency { get; set; } = null;
    public Guid? FxQuoteId { get; set; } = null;
    public Guid? OriginalTransactionId { get; set; } = null;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int SettingsVersion { get; set; } = 0;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? QuoteExpiresAt { get; set; } = null;

    public PaymentEvent()
    {
    }

    public PaymentEvent(PaymentEvent source)
    {
        PaymentId = source.PaymentId;
        UserId = source.UserId;
        RequestId = source.RequestId;
        Kind = source.Kind;
        AccountId = source.AccountId;
        RecipientAccountId = source.RecipientAccountId;
        Amount = source.Amount;
        State = source.State;
        Reference = source.Reference;
        Currency = source.Currency;
        Fee = source.Fee;
        TargetAmount = source.TargetAmount;
        TargetCurrency = source.TargetCurrency;
        FxQuoteId = source.FxQuoteId;
        OriginalTransactionId = source.OriginalTransactionId;
        SettingsVersion = source.SettingsVersion;
        QuoteExpiresAt = source.QuoteExpiresAt;
    }
}
