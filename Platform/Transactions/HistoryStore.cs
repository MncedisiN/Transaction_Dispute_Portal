using System.Text.Json;
using Dapper;
using DisputePortal.Platform.Contracts;
using DisputePortal.Platform.Infrastructure;
using DisputePortal.Platform.Infrastructure.Models;
using DisputePortal.Platform.Transactions.Interfaces;
using DisputePortal.Platform.Infrastructure.Sql;

namespace DisputePortal.Platform.Transactions;

public class HistoryStore(ServiceDatabase db) : IHistoryStore
{
    public static readonly string Schema = SqlScripts.ReadSqlScript("PostgreSql/Schema/Transactions/HistoryStore/CreateHistorySchema");

    public async Task<object> Import(AccountHistorySnapshot book, CancellationToken ct)
    {
        book.ValidateAccountHistorySnapshot();
        await using var c = await db.OpenServiceDatabaseConnection(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await c.ExecuteAsync(new CommandDefinition(
            """
            SELECT
                pg_advisory_xact_lock(hashtext(@Id))
            """,
            new
            {
                Id = "history-import:" + book.AccountId
            },
            tx,
            cancellationToken: ct));
        var old = await c.ExecuteScalarAsync<string?>(new CommandDefinition(
            """
            SELECT
                "sourceHash"
            FROM transactions."historyImports"
            WHERE "publicId" = @AccountId
            """,
            book,
            tx,
            cancellationToken: ct));
        if (old is not null)
        {
            if (old != book.Hash)
            {
                throw new PlatformException(409, "import_conflict", "The approved history hash differs.");
            }

            await tx.CommitAsync(ct);
            return new
            {
                accountId = book.AccountId,
                transactions = book.Transactions.Length,
                replayed = true
            };
        }

        foreach (var item in book.Transactions)
        {
            var record = new TransactionRecord
            {
                Id = item.Id,
                PaymentId = Guid.Empty,
                UserId = book.UserId,
                Kind = item.Type,
                Reference = item.Reference,
                AccountId = book.AccountId,
                RecipientAccountId = null,
                Amount = item.Amount,
                Currency = item.Currency,
                TargetAmount = null,
                TargetCurrency = null,
                Fee = 0,
                Status = item.Status == "Posted" ? "Completed" : item.Status,
                FailureCode = null,
                CreatedAt = item.OccurredAt,
                UpdatedAt = item.OccurredAt,
                Direction = item.Direction,
                Description = item.Description
            };
            await c.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO transactions."priorTransactions"(
                    "publicId",
                    "customerPublicId",
                    "accountPublicId",
                    "sourceHash",
                    body,
                    "occurredAt"
                )
                VALUES (
                    @Id,
                    @User,
                    @Account,
                    @Hash,
                    CAST(@Body AS jsonb),
                    @At
                )
                """,
                new
                {
                    item.Id,
                    User = book.UserId,
                    Account = book.AccountId,
                    book.Hash,
                    Body = JsonSerializer.Serialize(record, DomainEvent.Json),
                    At = item.OccurredAt.ToUniversalTime()
                },
                tx,
                cancellationToken: ct));
        }

        await c.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO transactions."historyImports"(
                "publicId",
                "sourceHash",
                "snapshotPublicId",
                "transactionCount"
            )
            VALUES (
                @AccountId,
                @Hash,
                @SnapshotId,
                @Count
            )
            """,
            new
            {
                book.AccountId,
                book.Hash,
                book.SnapshotId,
                Count = book.Transactions.Length
            },
            tx,
            cancellationToken: ct));
        await ServiceDatabase.InsertServiceAuditEvent(
            c,
            tx,
            book.ActorId,
            book.SourceSystem == "assessment" ? "AssessmentTransactionsImported" : "PriorHistoryImported",
            book.AccountId,
            "Approved source book hash: " + book.Hash,
            ct);
        await tx.CommitAsync(ct);
        return new
        {
            accountId = book.AccountId,
            transactions = book.Transactions.Length,
            replayed = false
        };
    }

    public async Task<TransactionRecord[]> GetCustomerTransactionHistory(Guid user, CancellationToken ct)
    {
        await using var c = await db.OpenServiceDatabaseConnection(ct);
        return (await c.QueryAsync<string>(new CommandDefinition(
            """
            SELECT
                body::text
            FROM transactions."priorTransactions"
            WHERE "customerPublicId" = @User
            ORDER BY "occurredAt" DESC, "publicId" DESC
            LIMIT 250
            """,
            new
            {
                User = user
            },
            cancellationToken: ct))).Select(x => JsonSerializer.Deserialize<TransactionRecord>(x, DomainEvent.Json)!).ToArray();
    }

    public async Task<TransactionRecord?> GetTransactionHistoryEntry(Guid id, Guid? user, CancellationToken ct)
    {
        await using var c = await db.OpenServiceDatabaseConnection(ct);
        var body = await c.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            """
            SELECT
                body::text
            FROM transactions."priorTransactions"
            WHERE "publicId" = @Id
            AND(@User IS NULL
                OR "customerPublicId" = @User)
            """,
            new
            {
                Id = id,
                User = user
            },
            cancellationToken: ct));
        return body is null ? null : JsonSerializer.Deserialize<TransactionRecord>(body, DomainEvent.Json);
    }
}
