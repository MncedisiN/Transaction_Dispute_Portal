namespace DisputePortal.Api.Models.Responses;

public class AccountFeedDto
{
    public bool Connected { get; set; }
    public bool CanRefresh { get; set; }
    public string? Provider { get; set; }
    public DateTimeOffset? LastSyncedAt { get; set; }

    public AccountFeedDto()
    {
    }

    public AccountFeedDto(bool Connected, bool CanRefresh, string? Provider, DateTimeOffset? LastSyncedAt)
    {
        this.Connected = Connected;
        this.CanRefresh = CanRefresh;
        this.Provider = Provider;
        this.LastSyncedAt = LastSyncedAt;
    }
}
