using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using DisputePortal.Platform.Contracts;
using DisputePortal.Platform.Infrastructure;
using DisputePortal.Platform.Infrastructure.Interfaces;
using DisputePortal.Platform.Infrastructure.Sql;

namespace DisputePortal.Platform.Transactions;

public class ReceivedActivityStore(ServiceDatabase db) : IDomainEventConsumer
{
    public static readonly string Schema = SqlScripts.ReadSqlScript("PostgreSql/Schema/Transactions/ReceivedActivityStore/CreateReceivedActivitySchema");
    public string[] Subscriptions => [EventTypes.LedgerTransactionPosted];

    public Task Handle(DomainEvent value, CancellationToken ct) => db.ProcessEventWithInboxDeduplication(
        value,
        async (c, tx) =>
    {
        var posted = value.DeserializePayload<LedgerPostedEvent>();
        if (posted.Kind != "Transfer")
        {
            return;
        }

        var body = await c.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
                """
                SELECT
                    "paymentBody"::text
                FROM transactions."transactions"
                WHERE "publicId" = @Id
                """,
                new
                {
                    Id = posted.TransactionId
                },
                tx,
                cancellationToken: ct));
        if (body is null)
        {
            return;
        }

        var instruction = JsonSerializer.Deserialize<PaymentEvent>(body, DomainEvent.Json)!;
        if (instruction.RecipientAccountId is not { } targetId)
        {
            return;
        }

        var target = posted.Balances.Single(p => p.AccountId == targetId);
        var id = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(posted.TransactionId + ":credit:" + targetId))[..16]);
        var record = new TransactionRecord
        {
            Id = id,
            PaymentId = instruction.PaymentId,
            UserId = target.UserId,
            Kind = "Transfer",
            Reference = instruction.Reference + "-IN",
            AccountId = targetId,
            RecipientAccountId = null,
            Amount = instruction.TargetAmount ?? instruction.Amount,
            Currency = target.Currency,
            TargetAmount = null,
            TargetCurrency = null,
            Fee = 0,
            Status = "Completed",
            FailureCode = null,
            CreatedAt = value.Timestamp,
            UpdatedAt = value.Timestamp,
            Direction = "Credit",
            Description = "Incoming transfer"
        };
        if (await c.ExecuteAsync(new CommandDefinition(
                SqlScripts.ReadSqlScript("PostgreSql/Queries/Transactions/ReceivedActivityStore/InsertReceivedTransaction"),
                new
                {
                    Id = id,
                    Transaction = posted.TransactionId,
                    AccountId = targetId,
                    target.UserId,
                    Body = JsonSerializer.Serialize(record, DomainEvent.Json),
                    At = value.Timestamp
                },
                tx,
                cancellationToken: ct)) == 1)
        {
            await ServiceDatabase.SaveDomainEventToOutbox(
                    c,
                    tx,
                    DomainEvent.CreateDomainEvent(
                        "transactions",
                        "transaction.received",
                        record,
                        value.CorrelationId,
                        value.EventId,
                        value.TransactionReference),
                    ct);
        }
    },
        ct);

    public async Task<TransactionRecord[]> GetReceivedAccountActivity(Guid user, CancellationToken ct)
    {
        await using var c = await db.OpenServiceDatabaseConnection(ct);
        return (await c.QueryAsync<string>(new CommandDefinition(
            """
            SELECT
                body::text
            FROM transactions."receivedTransactions"
            WHERE "customerPublicId" = @User
            ORDER BY "occurredAt" DESC
            LIMIT 250
            """,
            new
            {
                User = user
            },
            cancellationToken: ct))).Select(x => JsonSerializer.Deserialize<TransactionRecord>(x, DomainEvent.Json)!).ToArray();
    }

    public async Task<TransactionRecord?> GetReceivedActivityById(Guid id, Guid? user, CancellationToken ct)
    {
        await using var c = await db.OpenServiceDatabaseConnection(ct);
        var body = await c.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            """
            SELECT
                body::text
            FROM transactions."receivedTransactions"
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
