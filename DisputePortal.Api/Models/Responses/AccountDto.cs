namespace DisputePortal.Api.Models.Responses;

public class AccountDto
{
    public Guid Id { get; set; }
    public string AccountType { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public string MaskedAccountNumber { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset RegisteredAt { get; set; }
    public string? AccountNumber { get; set; }

    public AccountDto()
    {
    }

    public AccountDto(
        Guid Id,
        string AccountType,
        string Currency,
        string MaskedAccountNumber,
        string Status,
        DateTimeOffset RegisteredAt,
        string? AccountNumber = null)
    {
        this.Id = Id;
        this.AccountType = AccountType;
        this.Currency = Currency;
        this.MaskedAccountNumber = MaskedAccountNumber;
        this.Status = Status;
        this.RegisteredAt = RegisteredAt;
        this.AccountNumber = AccountNumber;
    }
}
