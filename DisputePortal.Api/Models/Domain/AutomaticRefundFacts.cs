namespace DisputePortal.Api.Models.Domain;

public class AutomaticRefundFacts
{
    public decimal Amount { get; set; }
    public string Currency { get; set; } = null!;
    public string AccountCurrency { get; set; } = null!;
    public string Type { get; set; } = null!;
    public string Direction { get; set; } = null!;
    public string Status { get; set; } = null!;
    public DateTimeOffset TransactionAt { get; set; }
    public DateTimeOffset OriginalAt { get; set; }
    public bool Active { get; set; }
    public bool ContactVerified { get; set; }
    public bool TrustedLocalProof { get; set; }
    public bool MatchesOriginal { get; set; }
    public bool OriginalDisputed { get; set; }
    public bool FraudOpen { get; set; }
    public bool AlreadyRefunded { get; set; }
    public decimal RefundedToday { get; set; }

    public AutomaticRefundFacts()
    {
    }

    public AutomaticRefundFacts(AutomaticRefundFacts source)
    {
        Amount = source.Amount;
        Currency = source.Currency;
        AccountCurrency = source.AccountCurrency;
        Type = source.Type;
        Direction = source.Direction;
        Status = source.Status;
        TransactionAt = source.TransactionAt;
        OriginalAt = source.OriginalAt;
        Active = source.Active;
        ContactVerified = source.ContactVerified;
        TrustedLocalProof = source.TrustedLocalProof;
        MatchesOriginal = source.MatchesOriginal;
        OriginalDisputed = source.OriginalDisputed;
        FraudOpen = source.FraudOpen;
        AlreadyRefunded = source.AlreadyRefunded;
        RefundedToday = source.RefundedToday;
    }
}
