namespace DisputePortal.Api.Models.Responses;

public class RefundRecallDto
{
    public Guid Id { get; set; }
    public Guid DisputeId { get; set; }
    public Guid CreditTransactionId { get; set; }
    public Guid ReversalTransactionId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset? PostedAt { get; set; }
    public string Reason { get; set; } = string.Empty;
    public bool Replayed { get; set; }
    public string Status { get; set; } = "Posted";

    public RefundRecallDto()
    {
    }

    public RefundRecallDto(
        Guid Id,
        Guid DisputeId,
        Guid CreditTransactionId,
        Guid ReversalTransactionId,
        decimal Amount,
        string Currency,
        DateTimeOffset? PostedAt,
        string Reason,
        bool Replayed = false,
        string Status = "Posted")
    {
        this.Id = Id;
        this.DisputeId = DisputeId;
        this.CreditTransactionId = CreditTransactionId;
        this.ReversalTransactionId = ReversalTransactionId;
        this.Amount = Amount;
        this.Currency = Currency;
        this.PostedAt = PostedAt;
        this.Reason = Reason;
        this.Replayed = Replayed;
        this.Status = Status;
    }
}
