namespace DisputePortal.Api.Models.Responses;

public class AccountBalanceDto
{
    public Guid AccountId { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal? CurrentBalance { get; set; }
    public decimal? AvailableBalance { get; set; }
    public decimal? ReservedAmount { get; set; }
    public bool BalanceKnown { get; set; }
    public string Status { get; set; } = "Active";
    public string AccountType { get; set; } = "Cheque";

    public AccountBalanceDto()
    {
    }

    public AccountBalanceDto(
        Guid AccountId,
        string Currency,
        decimal? CurrentBalance,
        decimal? AvailableBalance,
        decimal? ReservedAmount,
        bool BalanceKnown,
        string Status = "Active",
        string AccountType = "Cheque")
    {
        this.AccountId = AccountId;
        this.Currency = Currency;
        this.CurrentBalance = CurrentBalance;
        this.AvailableBalance = AvailableBalance;
        this.ReservedAmount = ReservedAmount;
        this.BalanceKnown = BalanceKnown;
        this.Status = Status;
        this.AccountType = AccountType;
    }
}
