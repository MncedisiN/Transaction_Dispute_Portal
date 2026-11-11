namespace DisputePortal.Api.Models.Domain;

public class DisputeRiskPolicy
{
    public int Version { get; set; }
    public decimal HighValueThreshold { get; set; }
    public int ExtraCheckAfterDays { get; set; }
    public int DebitOrderWindowDays { get; set; }
    public decimal DebitOrderAppLimit { get; set; }

    public void ValidateDisputeRiskPolicy()
    {
        if (Version < 1
            || !DisputePolicyLimits.ValidMoney(HighValueThreshold)
            || ExtraCheckAfterDays < 0
            || ExtraCheckAfterDays > DisputePolicyLimits.MaximumDays
            || DebitOrderWindowDays < 1
            || DebitOrderWindowDays > DisputePolicyLimits.MaximumDays
            || !DisputePolicyLimits.ValidMoney(DebitOrderAppLimit))
        {
            throw new ArgumentException("Use positive amounts up to 1,000,000,000,000 with at most two decimal places, an extra-check age of 0–36,500 days and a dispute window of 1–36,500 days.");
        }
    }

    public DisputeRiskDecision AssessTransactionDisputeRisk(
        decimal amount,
        string currency,
        string transactionType,
        DateTimeOffset transactionAt,
        DateTimeOffset now,
        TimeZoneInfo businessTimeZone,
        string? scheme = null,
        string? mandate = null)
    {
        var days = (TimeZoneInfo.ConvertTime(now, businessTimeZone).Date - TimeZoneInfo.ConvertTime(transactionAt, businessTimeZone).Date).Days;
        var debitOrder = transactionType == "DebitOrder";
        if (days < 0)
        {
            return new global::DisputePortal.Api.Models.Domain.DisputeRiskDecision
            {
                Allowed = false,
                RequiresVerification = false,
                AssistedReview = false,
                AgeDays = days,
                PolicyVersion = Version,
                Message = "This transaction is not ready for a dispute."
            };
        }

        if (debitOrder && days > DebitOrderWindowDays)
        {
            return new global::DisputePortal.Api.Models.Domain.DisputeRiskDecision
            {
                Allowed = false,
                RequiresVerification = false,
                AssistedReview = true,
                AgeDays = days,
                PolicyVersion = Version,
                Message = "The online debit-order window has closed. Contact support for guidance."
            };
        }

        if (debitOrder && scheme == "DebiCheck" && mandate == "Valid")
        {
            return new global::DisputePortal.Api.Models.Domain.DisputeRiskDecision
            {
                Allowed = false,
                RequiresVerification = false,
                AssistedReview = true,
                AgeDays = days,
                PolicyVersion = Version,
                Message = "This DebiCheck collection matches an authorised mandate. Contact support if you believe it was unauthorised."
            };
        }

        var assisted = debitOrder && (amount >= DebitOrderAppLimit || scheme == "DebiCheck");
        var check = amount >= HighValueThreshold || days > ExtraCheckAfterDays || currency != "ZAR" || assisted;
        return new global::DisputePortal.Api.Models.Domain.DisputeRiskDecision
        {
            Allowed = true,
            RequiresVerification = check,
            AssistedReview = assisted,
            AgeDays = days,
            PolicyVersion = Version,
            Message = assisted ? "A specialist will review this debit order. Verify your identity to continue." : check ? "An extra identity check is needed for this transaction." : "Ready to submit for review."
        };
    }

    public DisputeRiskPolicy()
    {
    }

    public DisputeRiskPolicy(DisputeRiskPolicy source)
    {
        Version = source.Version;
        HighValueThreshold = source.HighValueThreshold;
        ExtraCheckAfterDays = source.ExtraCheckAfterDays;
        DebitOrderWindowDays = source.DebitOrderWindowDays;
        DebitOrderAppLimit = source.DebitOrderAppLimit;
    }
}
