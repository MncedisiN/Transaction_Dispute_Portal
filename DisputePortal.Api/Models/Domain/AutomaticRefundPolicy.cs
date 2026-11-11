namespace DisputePortal.Api.Models.Domain;

public class AutomaticRefundPolicy
{
    public const string RuleCode = "confirmed-duplicate-v1";
    public const string DebitOrderRuleCode = "debit-order-refund-v1";
    public int Version { get; set; }
    public bool Enabled { get; set; }
    public bool ContactVerificationRequired { get; set; } = true;
    public decimal MaximumAmount { get; set; }
    public int MaximumAgeHours { get; set; }
    public decimal DailyLimit { get; set; }
    public int DuplicateWindowMinutes { get; set; }

    public bool IsValid() => Version > 0
        && DisputePolicyLimits.ValidMoney(MaximumAmount)
        && MaximumAgeHours is >= 1 and <= DisputePolicyLimits.MaximumHours
        && DuplicateWindowMinutes is >= 1 and <= 1440
        && DailyLimit >= MaximumAmount
        && DisputePolicyLimits.ValidMoney(DailyLimit);

    public bool Approves(AutomaticRefundFacts f, DateTimeOffset now) => IsValid()
        && Enabled
        && f.Active
        && (!ContactVerificationRequired || f.ContactVerified)
        && f.TrustedLocalProof
        && f.MatchesOriginal
        && !f.OriginalDisputed
        && !f.FraudOpen
        && !f.AlreadyRefunded
        && !string.IsNullOrWhiteSpace(f.AccountCurrency)
        && f.Currency == f.AccountCurrency
        && f.Type is ("CardPurchase" or "CreditCardPayment")
        && f.Direction == "Debit"
        && f.Status == "Posted"
        && f.Amount > 0
        && f.Amount <= MaximumAmount
        && f.TransactionAt <= now
        && f.TransactionAt >= now.AddHours(-MaximumAgeHours)
        && f.OriginalAt <= f.TransactionAt
        && f.OriginalAt >= f.TransactionAt.AddMinutes(-DuplicateWindowMinutes)
        && f.RefundedToday + f.Amount <= DailyLimit;

    public bool ApprovesDebitOrder(AutomaticRefundFacts f, string? scheme, string? mandate, DateTimeOffset now) => IsValid()
        && Enabled
        && f.Active
        && (!ContactVerificationRequired || f.ContactVerified)
        && f.TrustedLocalProof
        && !f.FraudOpen
        && !f.AlreadyRefunded
        && !string.IsNullOrWhiteSpace(f.AccountCurrency)
        && f.Currency == f.AccountCurrency
        && f.Type == "DebitOrder"
        && f.Direction == "Debit"
        && f.Status == "Posted"
        && scheme is ("EFT" or "RM")
        && mandate is ("Unknown" or "Disputed")
        && f.Amount > 0
        && f.Amount <= MaximumAmount
        && f.TransactionAt <= now
        && f.TransactionAt >= now.AddHours(-MaximumAgeHours)
        && f.RefundedToday + f.Amount <= DailyLimit;

    public AutomaticRefundPolicy()
    {
    }

    public AutomaticRefundPolicy(AutomaticRefundPolicy source)
    {
        Version = source.Version;
        Enabled = source.Enabled;
        ContactVerificationRequired = source.ContactVerificationRequired;
        MaximumAmount = source.MaximumAmount;
        MaximumAgeHours = source.MaximumAgeHours;
        DailyLimit = source.DailyLimit;
        DuplicateWindowMinutes = source.DuplicateWindowMinutes;
    }
}
