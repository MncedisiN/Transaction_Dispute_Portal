using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DisputePortal.Platform.Contracts;

public class AccountHistorySnapshot
{
    public Guid SnapshotId { get; set; }
    public Guid ActorId { get; set; }
    public string SourceSystem { get; set; } = null!;
    public DateTimeOffset CapturedAt { get; set; }
    public Guid AccountId { get; set; }
    public Guid UserId { get; set; }
    public string Number { get; set; } = null!;
    public string AccountType { get; set; } = null!;
    public string CountryCode { get; set; } = null!;
    public string Currency { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public decimal ExpectedBalance { get; set; }
    public decimal ExpectedReserved { get; set; }
    public PriorTransaction[] Transactions { get; set; } = null!;
    public string? Hash { get; set; } = null;

    public string ComputeHash() => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
        new global::DisputePortal.Platform.Contracts.AccountHistorySnapshot(this) { Hash = null },
        DomainEvent.Json))));

    public void ValidateAccountHistorySnapshot()
    {
        if (SnapshotId == Guid.Empty
            || ActorId == Guid.Empty
            || AccountId == Guid.Empty
            || UserId == Guid.Empty
            || SourceSystem is not ("dispute-portal-sql" or "assessment")
            || Transactions.Length > 10000
            || Hash != ComputeHash()
            || Number.Length is < 8 or > 24
            || Number.Any(c => !char.IsAsciiDigit(c))
            || TransactionIds.FindDuplicates(Transactions.Select(t => t.Id)).Count > 0
            || Transactions.Any(t => t.Id == Guid.Empty
            || t.Currency != Currency
            || t.Amount < 0
            || t.Unknown != 0
            || t.Direction is not ("Debit" or "Credit")
            || t.Status is not ("Posted" or "Pending" or "Reversed")
            || t.CurrentDelta != (t.Status == "Posted" ? (t.Direction == "Credit" ? t.Amount : -t.Amount) : 0)
            || t.ReservedDelta != (t.Status == "Pending"
            && t.Direction == "Debit" ? t.Amount : 0))
            || Transactions.Sum(t => t.CurrentDelta) != ExpectedBalance
            || Transactions.Sum(t => t.ReservedDelta) != ExpectedReserved
            || ExpectedReserved < 0)
        {
            throw new InvalidOperationException("The approved prior book does not reconcile or its hash changed.");
        }
    }

    public AccountHistorySnapshot()
    {
    }

    public AccountHistorySnapshot(AccountHistorySnapshot source)
    {
        SnapshotId = source.SnapshotId;
        ActorId = source.ActorId;
        SourceSystem = source.SourceSystem;
        CapturedAt = source.CapturedAt;
        AccountId = source.AccountId;
        UserId = source.UserId;
        Number = source.Number;
        AccountType = source.AccountType;
        CountryCode = source.CountryCode;
        Currency = source.Currency;
        CreatedAt = source.CreatedAt;
        ExpectedBalance = source.ExpectedBalance;
        ExpectedReserved = source.ExpectedReserved;
        Transactions = source.Transactions;
        Hash = source.Hash;
    }
}
