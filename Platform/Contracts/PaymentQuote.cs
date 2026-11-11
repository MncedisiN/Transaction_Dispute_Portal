namespace DisputePortal.Platform.Contracts;

public class PaymentQuote
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Kind { get; set; } = null!;
    public Guid AccountId { get; set; }
    public Guid? RecipientAccountId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = null!;
    public decimal Fee { get; set; }
    public decimal? TargetAmount { get; set; }
    public string? TargetCurrency { get; set; }
    public Guid? FxQuoteId { get; set; }
    public int SettingsVersion { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public decimal? ExchangeRate { get; set; } = null;
    public string? RateSource { get; set; } = null;
    public DateTimeOffset? RatePublishedAt { get; set; } = null;
    public DateTimeOffset? RateRetrievedAt { get; set; } = null;
    public Guid? RateId { get; set; } = null;
    public decimal? ReferenceRate { get; set; } = null;
    public decimal SpreadBps { get; set; } = 0;
    public string AmountExact => Amount.ToString(System.Globalization.CultureInfo.InvariantCulture);
    public string FeeExact => Fee.ToString(System.Globalization.CultureInfo.InvariantCulture);
    public string TotalDebitExact => (Amount + Fee).ToString(System.Globalization.CultureInfo.InvariantCulture);
    public string? TargetAmountExact => TargetAmount?.ToString(System.Globalization.CultureInfo.InvariantCulture);
    public string? ExchangeRateExact => ExchangeRate?.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public PaymentQuote()
    {
    }

    public PaymentQuote(PaymentQuote source)
    {
        Id = source.Id;
        UserId = source.UserId;
        Kind = source.Kind;
        AccountId = source.AccountId;
        RecipientAccountId = source.RecipientAccountId;
        Amount = source.Amount;
        Currency = source.Currency;
        Fee = source.Fee;
        TargetAmount = source.TargetAmount;
        TargetCurrency = source.TargetCurrency;
        FxQuoteId = source.FxQuoteId;
        SettingsVersion = source.SettingsVersion;
        ExpiresAt = source.ExpiresAt;
        ExchangeRate = source.ExchangeRate;
        RateSource = source.RateSource;
        RatePublishedAt = source.RatePublishedAt;
        RateRetrievedAt = source.RateRetrievedAt;
        RateId = source.RateId;
        ReferenceRate = source.ReferenceRate;
        SpreadBps = source.SpreadBps;
    }
}
