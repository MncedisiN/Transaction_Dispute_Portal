namespace DisputePortal.Api.Models.Responses;

public class StaffTransactionDto
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public Guid AccountId { get; set; }
    public string AccountType { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
    public DateTimeOffset TransactionAt { get; set; }
    public string Description { get; set; } = string.Empty;
    public string MerchantName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Direction { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool DisputeEligible { get; set; }
    public Guid? DisputeId { get; set; }
    public string? RefundReference { get; set; }

    public StaffTransactionDto()
    {
    }

    public StaffTransactionDto(
        Guid Id,
        Guid CustomerId,
        string CustomerName,
        string? Email,
        Guid AccountId,
        string AccountType,
        string AccountNumber,
        string Reference,
        DateTimeOffset TransactionAt,
        string Description,
        string MerchantName,
        decimal Amount,
        string Currency,
        string Type,
        string Direction,
        string Status,
        bool DisputeEligible,
        Guid? DisputeId,
        string? RefundReference = null)
    {
        this.Id = Id;
        this.CustomerId = CustomerId;
        this.CustomerName = CustomerName;
        this.Email = Email;
        this.AccountId = AccountId;
        this.AccountType = AccountType;
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
        this.DisputeId = DisputeId;
        this.RefundReference = RefundReference;
    }
}
