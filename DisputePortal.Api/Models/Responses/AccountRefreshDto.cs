namespace DisputePortal.Api.Models.Responses;

public class AccountRefreshDto
{
    public int TransactionsProcessed { get; set; }
    public bool MoreAvailable { get; set; }
    public DateTimeOffset? LastSyncedAt { get; set; }

    public AccountRefreshDto()
    {
    }

    public AccountRefreshDto(int TransactionsProcessed, bool MoreAvailable, DateTimeOffset? LastSyncedAt)
    {
        this.TransactionsProcessed = TransactionsProcessed;
        this.MoreAvailable = MoreAvailable;
        this.LastSyncedAt = LastSyncedAt;
    }
}
