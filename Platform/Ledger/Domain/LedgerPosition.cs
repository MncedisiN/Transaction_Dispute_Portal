namespace DisputePortal.Platform.Ledger.Domain;

public class LedgerPosition
{
    public Guid AccountId { get; set; }
    public Guid UserId { get; set; }
    public string Currency { get; set; } = null!;
    public string CountryCode { get; set; } = null!;
    public string Kind { get; set; } = null!;
    public string Status { get; set; } = null!;
    public decimal Balance { get; set; }
    public decimal Reserved { get; set; }
    public long Version { get; set; }
    public long InternalId { get; set; } = 0;
    public int CountryPolicyVersion { get; set; } = 0;

    public LedgerPosition()
    {
    }

    public LedgerPosition(LedgerPosition source)
    {
        AccountId = source.AccountId;
        UserId = source.UserId;
        Currency = source.Currency;
        CountryCode = source.CountryCode;
        Kind = source.Kind;
        Status = source.Status;
        Balance = source.Balance;
        Reserved = source.Reserved;
        Version = source.Version;
        InternalId = source.InternalId;
        CountryPolicyVersion = source.CountryPolicyVersion;
    }
}
