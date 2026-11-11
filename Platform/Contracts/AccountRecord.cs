namespace DisputePortal.Platform.Contracts;

public class AccountRecord
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Number { get; set; } = null!;
    public string Type { get; set; } = null!;
    public string CountryCode { get; set; } = null!;
    public string Currency { get; set; } = null!;
    public string Status { get; set; } = null!;
    public decimal LedgerBalance { get; set; }
    public decimal ReservedBalance { get; set; }
    public long Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public int CountryPolicyVersion { get; set; } = 0;
    public decimal AvailableBalance => LedgerBalance - ReservedBalance;
    public string AvailableBalanceExact => AvailableBalance.ToString(System.Globalization.CultureInfo.InvariantCulture);
    public string LedgerBalanceExact => LedgerBalance.ToString(System.Globalization.CultureInfo.InvariantCulture);
    public string ReservedBalanceExact => ReservedBalance.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public AccountRecord()
    {
    }

    public AccountRecord(AccountRecord source)
    {
        Id = source.Id;
        UserId = source.UserId;
        Number = source.Number;
        Type = source.Type;
        CountryCode = source.CountryCode;
        Currency = source.Currency;
        Status = source.Status;
        LedgerBalance = source.LedgerBalance;
        ReservedBalance = source.ReservedBalance;
        Version = source.Version;
        CreatedAt = source.CreatedAt;
        CountryPolicyVersion = source.CountryPolicyVersion;
    }
}
