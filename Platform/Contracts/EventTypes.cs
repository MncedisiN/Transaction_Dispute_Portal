namespace DisputePortal.Platform.Contracts;

public static class EventTypes
{
    public const string CaseStatusesChanged = "dispute.case-statuses.changed";
    public const string UserRegistered = "user.registered", AccountCreated = "account.created", AccountActivated = "account.activated", TransactionInitiated = "transaction.initiated", TransactionValidated = "transaction.validated", TransactionProcessing = "transaction.processing", TransactionCompleted = "transaction.completed", TransactionFailed = "transaction.failed", LedgerEntryCreated = "ledger.entry.created", LedgerTransactionPosted = "ledger.transaction.posted", BalanceUpdated = "account.balance.updated", PaymentInitiated = "payment.initiated", PaymentCompleted = "payment.completed", PaymentFailed = "payment.failed", NotificationRequested = "notification.requested", NotificationSent = "notification.sent";
    public const string AssessmentAccountRequested = "assessment.account.requested";
    public static readonly IReadOnlyDictionary<string, string> Sources = new Dictionary<string, string>
    {
        [UserRegistered] = "identity",
        [AssessmentAccountRequested] = "identity",
        [CaseStatusesChanged] = "identity",
        [AccountCreated] = "accounts",
        [AccountActivated] = "accounts",
        [TransactionInitiated] = "transactions",
        [TransactionValidated] = "transactions",
        [TransactionProcessing] = "transactions",
        [TransactionCompleted] = "transactions",
        [TransactionFailed] = "transactions",
        [LedgerEntryCreated] = "ledger",
        [LedgerTransactionPosted] = "ledger",
        [BalanceUpdated] = "accounts",
        [PaymentInitiated] = "payments",
        [PaymentCompleted] = "payments",
        [PaymentFailed] = "payments",
        [NotificationRequested] = "notifications",
        [NotificationSent] = "notifications",
        ["ledger.account.created"] = "ledger",
        ["ledger.reservation.updated"] = "ledger",
        ["payment.authorized"] = "payments",
        ["configuration.updated"] = "configuration",
        ["reversal.preparation.requested"] = "scheduling",
        ["debt.reviewed"] = "debt",
        ["reversal.review.completed"] = "scheduling",
        ["dispute.financial.requested"] = "identity",
        ["transaction.received"] = "transactions",
        ["country.operation.settled"] = "identity",
        ["ledger.import.settled"] = "ledger"
    };
}
