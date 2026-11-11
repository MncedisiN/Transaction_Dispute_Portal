using System.Text.Json.Serialization;

namespace DisputePortal.Platform.Contracts;

public class PostJournalRequest
{
    public Guid TransactionId { get; set; }
    public Guid UserId { get; set; }
    public string Kind { get; set; } = null!;
    public string Reference { get; set; } = null!;
    public Guid AccountId { get; set; }
    public Guid? RecipientAccountId { get; set; }
    public decimal Amount { get; set; }
    public decimal Fee { get; set; }
    public string Currency { get; set; } = null!;
    public decimal? TargetAmount { get; set; }
    public string? TargetCurrency { get; set; }
    public Guid? QuoteId { get; set; }
    public Guid? OriginalTransactionId { get; set; } = null;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int SettingsVersion { get; set; } = 0;

    public PostJournalRequest()
    {
    }

    public PostJournalRequest(PostJournalRequest source)
    {
        TransactionId = source.TransactionId;
        UserId = source.UserId;
        Kind = source.Kind;
        Reference = source.Reference;
        AccountId = source.AccountId;
        RecipientAccountId = source.RecipientAccountId;
        Amount = source.Amount;
        Fee = source.Fee;
        Currency = source.Currency;
        TargetAmount = source.TargetAmount;
        TargetCurrency = source.TargetCurrency;
        QuoteId = source.QuoteId;
        OriginalTransactionId = source.OriginalTransactionId;
        SettingsVersion = source.SettingsVersion;
    }
}
