using System.Text.Json;
using Dapper;
using DisputePortal.Platform.Contracts;
using DisputePortal.Platform.Infrastructure;
using DisputePortal.Platform.ReversalReviews.Models;
using DisputePortal.Platform.ReversalReviews.Interfaces;
using DisputePortal.Platform.Infrastructure.Sql;

namespace DisputePortal.Platform.ReversalReviews;

public class ReversalReviewStore(ServiceDatabase db) : IReversalReviewStore
{
    public static readonly string Schema = SqlScripts.ReadSqlScript("PostgreSql/Schema/ReversalReviews/ReversalReviewStore/CreateReversalReviewSchema");

    public Task SaveReversalReviewRequest(DomainEvent e, CancellationToken ct) => db.ProcessEventWithInboxDeduplication(
        e,
        async (c, tx) =>
    {
        var r = e.DeserializePayload<ReversalPreparation>();
        if (r.RequestId == Guid.Empty
                || r.ActorId == Guid.Empty
                || r.OriginalTransactionId == Guid.Empty
                || r.Kind is not ("Refund" or "Recall"))
        {
            throw new InvalidOperationException("Invalid reversal preparation.");
        }

        var old = await c.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
                """
                SELECT
                    "requestBody"::text
                FROM debt."reversalReviews"
                WHERE "publicId" = @Id
                """,
                new
                {
                    Id = r.RequestId
                },
                tx,
                cancellationToken: ct));
        if (old is not null)
        {
            var previous = JsonSerializer.Deserialize<ReversalPreparation>(old, DomainEvent.Json);
            if (previous is null
                || previous.RequestId != r.RequestId
                || previous.OriginalTransactionId != r.OriginalTransactionId
                || previous.Kind != r.Kind
                || previous.ActorId != r.ActorId)
            {
                throw new InvalidOperationException("Conflicting reversal preparation.");
            }
        }

        await c.ExecuteAsync(new CommandDefinition(
                SqlScripts.ReadSqlScript("PostgreSql/Queries/ReversalReviews/ReversalReviewStore/InsertReversalReview"),
                new
                {
                    Id = r.RequestId,
                    Body = JsonSerializer.Serialize(r, DomainEvent.Json)
                },
                tx,
                cancellationToken: ct));
    },
        ct);

    public async Task<ReversalReviewWorkItem?> ClaimNextReversalReview(CancellationToken ct)
    {
        await using var c = await db.OpenServiceDatabaseConnection(ct);
        return await c.QuerySingleOrDefaultAsync<ReversalReviewWorkItem>(new CommandDefinition(
            SqlScripts.ReadSqlScript("PostgreSql/Queries/ReversalReviews/ReversalReviewStore/ClaimNextPendingReversalReview"),
            new
            {
                Lease = Guid.NewGuid()
            },
            cancellationToken: ct));
    }

    public async Task CompleteReversalReview(ReversalReviewWorkItem w, string status, string code, CancellationToken ct)
    {
        await using var c = await db.OpenServiceDatabaseConnection(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        if (await c.ExecuteAsync(new CommandDefinition(
            """
            UPDATE debt."reversalReviews"
            SET
                status = @Status,
                "decisionCode" = @Code,
                "leaseId" = NULL,
                "leaseUntil" = NULL
            WHERE "publicId" = @Id
            AND "leaseId" = @LeaseId
            """,
            new
            {
                w.Id,
                w.LeaseId,
                Status = status,
                Code = code
            },
            tx,
            cancellationToken: ct)) == 1)
        {
            await ServiceDatabase.SaveDomainEventToOutbox(
                c,
                tx,
                DomainEvent.CreateDomainEvent(
                    "debt",
                    "debt.reviewed",
                    new
                    {
                        requestId = w.Id,
                        status,
                        code
                    },
                    w.Id),
                ct);
            await ServiceDatabase.InsertServiceAuditEvent(c, tx, w.Request.ActorId, "ReversalReviewed", w.Id, code, ct);
        }

        await tx.CommitAsync(ct);
    }

    public async Task RetryReversalReview(ReversalReviewWorkItem w, CancellationToken ct)
    {
        await using var c = await db.OpenServiceDatabaseConnection(ct);
        await c.ExecuteAsync(new CommandDefinition(
            """
            UPDATE debt."reversalReviews"
            SET
                "leaseId" = NULL,
                "leaseUntil" = NULL,
                "nextAttemptAt" = now() + interval '15 seconds'
            WHERE "publicId" = @Id
            AND "leaseId" = @LeaseId
            """,
            w,
            cancellationToken: ct));
    }

    public async Task<object> GetReversalReviews(CancellationToken ct)
    {
        await using var c = await db.OpenServiceDatabaseConnection(ct);
        return await c.QueryAsync(new CommandDefinition(
            SqlScripts.ReadSqlScript("PostgreSql/Queries/ReversalReviews/ReversalReviewStore/GetLatestReversalReviews"),
            cancellationToken: ct));
    }
}
