using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using DisputePortal.Platform.Contracts;
using DisputePortal.Platform.Infrastructure;
using DisputePortal.Platform.Ledger.Domain;
using DisputePortal.Platform.Infrastructure.Sql;

namespace DisputePortal.Platform.Ledger;

internal static class JournalWriter
{
    internal static string Body(PostJournalRequest request) => JsonSerializer.Serialize(request, DomainEvent.Json);

    internal static string Hash(PostJournalRequest request) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Body(request))));

    internal static async Task<JournalResult> Persist(
        DbConnection c,
        DbTransaction tx,
        PostingPlan plan,
        Dictionary<Guid, LedgerPosition> positions,
        IReadOnlyDictionary<Guid, decimal> released,
        DateTimeOffset created,
        CancellationToken ct)
    {
        var r = plan.Request;
        var lines = plan.Lines;
        if (lines.Count < 2
            || TransactionIds.FindDuplicates(lines.Select(l => l.Id)).Count > 0
            || lines.Any(l => l.Amount <= 0
            || l.Side is not ("D" or "C")
            || !positions.TryGetValue(l.AccountId, out var p)
            || p.Currency != l.Currency)
            || lines.GroupBy(l => l.Currency).Any(g => g.Sum(l => l.Side == "D" ? l.Amount : -l.Amount) != 0))
        {
            throw new InvalidOperationException("A balanced journal requires valid unique entries in each account's currency.");
        }

        var journal = Guid.NewGuid();
        var journalId = await c.ExecuteScalarAsync<long>(new CommandDefinition(
            SqlScripts.ReadSqlScript("PostgreSql/Queries/Ledger/JournalWriter/InsertJournal"),
            new
            {
                Id = journal,
                r.TransactionId,
                r.Reference,
                r.Kind,
                Hash = Hash(r),
                Body = Body(r),
                Created = created
            },
            tx,
            cancellationToken: ct));
        foreach (var line in lines)
        {
            await c.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO ledger."entries"(
                    "publicId",
                    "journalId",
                    "accountId",
                    currency,
                    side,
                    amount,
                    "createdAt"
                )
                VALUES (
                    @Id,
                    @JournalId,
                    @AccountId,
                    @Currency,
                    @Side,
                    @Amount,
                    @Created
                )
                """,
                new
                {
                    line.Id,
                    JournalId = journalId,
                    AccountId = positions[line.AccountId].InternalId,
                    line.Currency,
                    line.Side,
                    line.Amount,
                    Created = created
                },
                tx,
                cancellationToken: ct));
            await ServiceDatabase.SaveDomainEventToOutbox(
                c,
                tx,
                DomainEvent.CreateDomainEvent(
                    "ledger",
                    EventTypes.LedgerEntryCreated,
                    new LedgerEntryEvent
                    {
                        JournalId = journal,
                        EntryId = line.Id,
                        AccountId = line.AccountId,
                        Currency = line.Currency,
                        Side = line.Side,
                        Amount = line.Amount
                    },
                    r.TransactionId,
                    reference: r.Reference),
                ct);
        }

        var snapshots = new List<BalanceSnapshot>();
        foreach (var group in lines.GroupBy(l => l.AccountId))
        {
            var p = positions[group.Key];
            var delta = group.Sum(l => l.Side == "C" ? l.Amount : -l.Amount);
            p = new global::DisputePortal.Platform.Ledger.Domain.LedgerPosition(p)
            {
                Balance = p.Balance + delta,
                Reserved = p.Reserved - (released.TryGetValue(p.AccountId, out var held) ? held : 0),
                Version = p.Version + 1
            };
            await c.ExecuteAsync(new CommandDefinition(
                """
                UPDATE ledger."accounts"
                SET
                    balance = @Balance,
                    reserved = @Reserved,
                    version = @Version
                WHERE id = @InternalId
                """,
                p,
                tx,
                cancellationToken: ct));
            positions[p.AccountId] = p;
            if (p.Kind == "Customer")
            {
                snapshots.Add(new global::DisputePortal.Platform.Contracts.BalanceSnapshot
                {
                    AccountId = p.AccountId,
                    UserId = p.UserId,
                    Currency = p.Currency,
                    LedgerBalance = p.Balance,
                    ReservedBalance = p.Reserved,
                    Version = p.Version
                });
            }
        }

        await ServiceDatabase.SaveDomainEventToOutbox(
            c,
            tx,
            DomainEvent.CreateDomainEvent(
                "ledger",
                EventTypes.LedgerTransactionPosted,
                new LedgerPostedEvent
                {
                    JournalId = journal,
                    TransactionId = r.TransactionId,
                    Kind = r.Kind,
                    Balances = snapshots.ToArray()
                },
                r.TransactionId,
                reference: r.Reference),
            ct);
        await c.ExecuteAsync(new CommandDefinition(
            """
            UPDATE ledger."journals"
            SET
                status = 'Posted'
            WHERE id = @Id
            """,
            new
            {
                Id = journalId
            },
            tx,
            cancellationToken: ct));
        await ServiceDatabase.InsertServiceAuditEvent(
            c,
            tx,
            null,
            "JournalPosted",
            journal,
            "Balanced immutable journal; the signed instruction identifies its customer and workflow.",
            ct);
        return new(journal, r.TransactionId, r.Reference, snapshots.ToArray());
    }
}
