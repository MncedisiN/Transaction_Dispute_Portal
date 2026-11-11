namespace DisputePortal.Api.Models.Responses;

public class BalanceOverviewDto
{
    public string? Currency { get; set; }
    public IReadOnlyList<AccountBalanceDto> Accounts { get; set; } = [];
    public decimal? CurrentBalance { get; set; }
    public decimal? AvailableBalance { get; set; }
    public int AccountCount { get; set; }
    public bool BalanceKnown { get; set; }

    public BalanceOverviewDto()
    {
    }

    public BalanceOverviewDto(
        IReadOnlyList<AccountBalanceDto> Accounts,
        decimal? CurrentBalance,
        decimal? AvailableBalance,
        int AccountCount,
        bool BalanceKnown)
    {
        this.Accounts = Accounts;
        this.CurrentBalance = CurrentBalance;
        this.AvailableBalance = AvailableBalance;
        this.AccountCount = AccountCount;
        this.BalanceKnown = BalanceKnown;
    }
}
