namespace DisputePortal.Api.Models.Domain;

public class CaseServicePolicy
{
    public decimal HighValueThreshold { get; set; } = 5000m;
    public int CriticalResponseHours { get; set; } = 1;
    public int CriticalResolutionHours { get; set; } = 24;
    public int HighResponseHours { get; set; } = 4;
    public int HighResolutionHours { get; set; } = 48;
    public int StandardResponseHours { get; set; } = 8;
    public int StandardResolutionHours { get; set; } = 72;
    public int LowResponseHours { get; set; } = 24;
    public int LowResolutionHours { get; set; } = 168;

    public (int Response, int Resolution) Targets(int priority) => priority switch
    {
        5 => (CriticalResponseHours, CriticalResolutionHours),
        4 => (HighResponseHours, HighResolutionHours),
        1 or 2 => (LowResponseHours, LowResolutionHours),
        _ => (StandardResponseHours, StandardResolutionHours)
    };

    public void ValidateCaseServiceRules()
    {
        if (HighValueThreshold <= 0)
        {
            throw new InvalidOperationException("The high-value threshold must be positive.");
        }

        foreach (var priority in new[]
        {
            1,
            3,
            4,
            5
        }

        )
        {
            var target = Targets(priority);
            if (target.Response <= 0 || target.Resolution < target.Response || target.Resolution > 8760)
            {
                throw new InvalidOperationException("Service targets must be positive, response must precede resolution, and resolution must not exceed one year.");
            }
        }
    }

    public CaseClassification ClassifyCaseReviewQueue(string category, string transactionType, decimal amount, string currency)
    {
        var fraud = category == "Unrecognized";
        var highValue = amount >= HighValueThreshold || currency != "ZAR";
        return new global::DisputePortal.Api.Models.Domain.CaseClassification
        {
            TransactionType = transactionType,
            QueueType = Route(category, transactionType),
            Severity = fraud ? (highValue ? "Critical" : "High") : highValue ? "High" : "Medium",
            Priority = fraud ? 5 : highValue ? 4 : 3
        };
    }

    public static bool IsClosed(string status) => status is "Resolved" or "Declined" or "Withdrawn";

    public static bool IsSupervisor(string role) => role is "super_admin" or "operations_manager";

    public static IReadOnlyList<string> Queues { get; } = Array.AsReadOnly(new[] { "Fraud", "Card", "DebitOrder", "Payments", "General" });

    public static string? SpecialistFor(string queue) => queue switch
    {
        "Fraud" => "fraud_agent",
        "Card" => "card_specialist",
        "DebitOrder" => "debit_order_specialist",
        "Payments" => "payments_specialist",
        "General" => "dispute_agent",
        _ => null
    };

    public static bool IsSpecialist(string role) => Queues.Any(queue => SpecialistFor(queue) == role);

    public static string Route(string category, string transactionType) => category == "Unrecognized" ? "Fraud" : transactionType switch
    {
        "DebitOrder" => "DebitOrder",
        "CardPurchase" or "CreditCardPayment" => "Card",
        "Transfer" or "CashWithdrawal" => "Payments",
        _ => "General"
    };

    public static bool CanBeAssigned(string role, string queue, int escalationLevel, bool supervisorSelfAssignment = false) => CanWork(
        role,
        queue) || IsSupervisor(role) && (escalationLevel > 0 || supervisorSelfAssignment);

    public static bool CanSee(string role, string queue) => IsSupervisor(role) || role == "audit_reader" || CanWork(role, queue);

    public static bool CanWork(string role, string queue) => SpecialistFor(queue) is { } specialist && role == specialist;
}
