namespace DisputePortal.Api.Models.Responses;

public class DisputeSummaryDto
{
    public Guid Id { get; set; }
    public string Reference { get; set; } = string.Empty;
    public Guid TransactionId { get; set; }
    public string TransactionDescription { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int Priority { get; set; }
    public int Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string? Resolution { get; set; }
    public string? TransactionType { get; set; }
    public string? TransactionTypeLabel { get; set; }

    public DisputeSummaryDto()
    {
    }

    public DisputeSummaryDto(
        Guid Id,
        string Reference,
        Guid TransactionId,
        string TransactionDescription,
        decimal Amount,
        string Currency,
        string Category,
        string Status,
        int Priority,
        int Version,
        DateTimeOffset CreatedAt,
        string? Resolution,
        string? TransactionType = null,
        string? TransactionTypeLabel = null)
    {
        this.Id = Id;
        this.Reference = Reference;
        this.TransactionId = TransactionId;
        this.TransactionDescription = TransactionDescription;
        this.Amount = Amount;
        this.Currency = Currency;
        this.Category = Category;
        this.Status = Status;
        this.Priority = Priority;
        this.Version = Version;
        this.CreatedAt = CreatedAt;
        this.Resolution = Resolution;
        this.TransactionType = TransactionType;
        this.TransactionTypeLabel = TransactionTypeLabel;
    }
}
