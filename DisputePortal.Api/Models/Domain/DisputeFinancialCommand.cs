namespace DisputePortal.Api.Models.Domain;

public class DisputeFinancialCommand
{
    public Guid TransactionId { get; set; }
    public Guid RequestId { get; set; }
    public Guid DisputeId { get; set; }
    public Guid OriginalTransactionId { get; set; }
    public Guid AccountId { get; set; }
    public Guid UserId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public Guid? ActorId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public int? PolicyVersion { get; set; }
    public int ExpectedVersion { get; set; }
    public Guid Correlation { get; set; }
    public string State { get; set; } = string.Empty;
    public string? FailureCode { get; set; }

    public DisputeFinancialCommand()
    {
    }

    public DisputeFinancialCommand(
        Guid TransactionId,
        Guid RequestId,
        Guid DisputeId,
        Guid OriginalTransactionId,
        Guid AccountId,
        Guid UserId,
        string Kind,
        decimal Amount,
        string Currency,
        Guid? ActorId,
        string Reason,
        int? PolicyVersion,
        int ExpectedVersion,
        Guid Correlation,
        string State,
        string? FailureCode)
    {
        this.TransactionId = TransactionId;
        this.RequestId = RequestId;
        this.DisputeId = DisputeId;
        this.OriginalTransactionId = OriginalTransactionId;
        this.AccountId = AccountId;
        this.UserId = UserId;
        this.Kind = Kind;
        this.Amount = Amount;
        this.Currency = Currency;
        this.ActorId = ActorId;
        this.Reason = Reason;
        this.PolicyVersion = PolicyVersion;
        this.ExpectedVersion = ExpectedVersion;
        this.Correlation = Correlation;
        this.State = State;
        this.FailureCode = FailureCode;
    }
}
