namespace DisputePortal.Api.Models.Domain;

public class BankFeedBinding
{
    public Guid AccountId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string ExternalAccountId { get; set; } = string.Empty;
    public string? Cursor { get; set; }
    public int Revision { get; set; }
    public DateTimeOffset? LastSyncedAt { get; set; }

    public BankFeedBinding()
    {
    }

    public BankFeedBinding(
        Guid AccountId,
        string Provider,
        string ExternalAccountId,
        string? Cursor,
        int Revision,
        DateTimeOffset? LastSyncedAt)
    {
        this.AccountId = AccountId;
        this.Provider = Provider;
        this.ExternalAccountId = ExternalAccountId;
        this.Cursor = Cursor;
        this.Revision = Revision;
        this.LastSyncedAt = LastSyncedAt;
    }

    public BankFeedBinding(BankFeedBinding other)
    {
        AccountId = other.AccountId;
        Provider = other.Provider;
        ExternalAccountId = other.ExternalAccountId;
        Cursor = other.Cursor;
        Revision = other.Revision;
        LastSyncedAt = other.LastSyncedAt;
    }
}
