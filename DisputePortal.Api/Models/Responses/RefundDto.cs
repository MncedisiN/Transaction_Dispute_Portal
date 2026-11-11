namespace DisputePortal.Api.Models.Responses;

public class RefundDto
{
    public Guid DisputeId { get; set; }
    public Guid CreditTransactionId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset? PostedAt { get; set; }
    public bool Automatic { get; set; }
    public bool Replayed { get; set; }
    public string? CreditReference { get; set; }
    public string? CustomerReference { get; set; }
    public string Status { get; set; } = "Posted";

    public RefundDto()
    {
    }

    public RefundDto(
        Guid DisputeId,
        Guid CreditTransactionId,
        decimal Amount,
        string Currency,
        DateTimeOffset? PostedAt,
        bool Automatic,
        bool Replayed = false,
        string? CreditReference = null,
        string? CustomerReference = null,
        string Status = "Posted")
    {
        this.DisputeId = DisputeId;
        this.CreditTransactionId = CreditTransactionId;
        this.Amount = Amount;
        this.Currency = Currency;
        this.PostedAt = PostedAt;
        this.Automatic = Automatic;
        this.Replayed = Replayed;
        this.CreditReference = CreditReference;
        this.CustomerReference = CustomerReference;
        this.Status = Status;
    }

    public RefundDto(RefundDto other)
    {
        DisputeId = other.DisputeId;
        CreditTransactionId = other.CreditTransactionId;
        Amount = other.Amount;
        Currency = other.Currency;
        PostedAt = other.PostedAt;
        Automatic = other.Automatic;
        Replayed = other.Replayed;
        CreditReference = other.CreditReference;
        CustomerReference = other.CustomerReference;
        Status = other.Status;
    }
}
