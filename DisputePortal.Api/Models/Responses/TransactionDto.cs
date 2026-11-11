namespace DisputePortal.Api.Models.Responses;

public class TransactionDto
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public string AccountNumber { get; set; } = string.Empty;
    public string? Reference { get; set; }
    public DateTimeOffset TransactionAt { get; set; }
    public string Description { get; set; } = string.Empty;
    public string MerchantName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Direction { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool DisputeEligible { get; set; }
    public bool HasActiveDispute { get; set; }
    public Guid? DisputeId { get; set; }
    public string? DisputeStatus { get; set; }
    public string? TypeLabel { get; set; }
    public string? AccountType { get; set; }

    public TransactionDto()
    {
    }

    public TransactionDto(
        Guid Id,
        Guid AccountId,
        string AccountNumber,
        string? Reference,
        DateTimeOffset TransactionAt,
        string Description,
        string MerchantName,
        decimal Amount,
        string Currency,
        string Type,
        string Direction,
        string Status,
        bool DisputeEligible,
        bool HasActiveDispute,
        Guid? DisputeId,
        string? DisputeStatus = null,
        string? TypeLabel = null,
        string? AccountType = null)
    {
        this.Id = Id;
        this.AccountId = AccountId;
        this.AccountNumber = AccountNumber;
        this.Reference = Reference;
        this.TransactionAt = TransactionAt;
        this.Description = Description;
        this.MerchantName = MerchantName;
        this.Amount = Amount;
        this.Currency = Currency;
        this.Type = Type;
        this.Direction = Direction;
        this.Status = Status;
        this.DisputeEligible = DisputeEligible;
        this.HasActiveDispute = HasActiveDispute;
        this.DisputeId = DisputeId;
        this.DisputeStatus = DisputeStatus;
        this.TypeLabel = TypeLabel;
        this.AccountType = AccountType;
    }
}
