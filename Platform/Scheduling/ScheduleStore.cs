using Dapper;
using DisputePortal.Platform.Contracts;
using DisputePortal.Platform.Infrastructure;
using DisputePortal.Platform.Infrastructure.Interfaces;
using DisputePortal.Platform.Infrastructure.Models;
using DisputePortal.Platform.Scheduling.Interfaces;
using DisputePortal.Platform.Infrastructure.Sql;

namespace DisputePortal.Platform.Scheduling;

public class ScheduleStore(ServiceDatabase db, IServiceClient client) : IScheduleStore
{
    public static readonly string Schema = SqlScripts.ReadSqlScript("PostgreSql/Schema/Scheduling/ScheduleStore/CreateScheduleSchema");

    public async Task<object> ScheduleReversalReview(ScheduleReversal r, Guid actor, CancellationToken ct)
    {
        if (r.RequestId == Guid.Empty
            || r.OriginalTransactionId == Guid.Empty
            || r.Kind is not ("Refund" or "Recall")
            || string.IsNullOrWhiteSpace(r.Reason)
            || r.Reason.Length > 500
            || r.DueAt < DateTimeOffset.UtcNow.AddMinutes(-1)
            || r.DueAt > DateTimeOffset.UtcNow.AddYears(2))
        {
            throw new PlatformException(400, "schedule_invalid", "Enter a valid reversal request, time and reason.");
        }

        // PostgreSQL stores timestamptz at microsecond precision. Compare the same
        // persisted instant on retries, without changing the caller's HTTP DTO.
        var dueAt = r.DueAt.ToUniversalTime();
        dueAt = dueAt.AddTicks(-(dueAt.Ticks % TimeSpan.TicksPerMicrosecond));
        var original = await client.SendServiceRequest<TransactionRecord>(
            "transactions",
            HttpMethod.Get,
            $"/transactions/{r.OriginalTransactionId}",
            ct: ct);
        if (original.Status != "Completed"
            || !(r.Kind == "Refund"
            && original.Direction == "Debit"
            && original.Kind is not ("Refund" or "Recall")
            || r.Kind == "Recall"
            && original.Kind == "Refund"))
        {
            throw new PlatformException(409, "reversal_ineligible", "The original transaction is ineligible.");
        }

        var account = await client.SendServiceRequest<AccountRecord>("accounts", HttpMethod.Get, $"/accounts/{original.AccountId}", ct: ct);
        var operation = await client.SendServiceRequest<CountryOperation>(
            "configuration",
            HttpMethod.Post,
            "/country-operations",
            new CountryOperationRequest
            {
                RequestId = r.RequestId,
                OperationId = r.RequestId,
                SourceKind = "Reversal",
                CountryCode = account.CountryCode,
                Currency = original.Currency,
                OriginalTransactionId = r.OriginalTransactionId
            },
            ct: ct);
        await using var c = await db.OpenServiceDatabaseConnection(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await c.ExecuteAsync(new CommandDefinition(
            """
            SELECT
                pg_advisory_xact_lock(hashtext(@Id))
            """,
            new
            {
                Id = r.RequestId.ToString()
            },
            tx,
            cancellationToken: ct));
        var old = await c.QuerySingleOrDefaultAsync<ScheduleReversal>(new CommandDefinition(
            """
            SELECT
                "publicId" AS RequestId,
                "originalTransactionPublicId" AS OriginalTransactionId,
                kind,
                "dueAt" AS DueAt,
                reason
            FROM scheduling."reversals"
            WHERE "publicId" = @RequestId
            """,
            r,
            tx,
            cancellationToken: ct));
        if (old is not null
            && (old.RequestId != r.RequestId
            || old.OriginalTransactionId != r.OriginalTransactionId
            || old.Kind != r.Kind
            || old.DueAt != dueAt
            || old.Reason != r.Reason))
        {
            throw new PlatformException(409, "request_conflict", "This schedule ID has different details.");
        }

        if (old is null)
        {
            if (operation.SettledAt is not null)
            {
                throw new PlatformException(409, "operation_settled", "This reversal request is already settled.");
            }

            await c.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO scheduling."reversals"(
                    "publicId",
                    "originalTransactionPublicId",
                    kind,
                    "actorPublicId",
                    "dueAt",
                    reason
                )
                VALUES (
                    @RequestId,
                    @OriginalTransactionId,
                    @Kind,
                    @Actor,
                    @DueAt,
                    @Reason
                )
                """,
                new
                {
                    r.RequestId,
                    r.OriginalTransactionId,
                    r.Kind,
                    Actor = actor,
                    DueAt = dueAt,
                    r.Reason
                },
                tx,
                cancellationToken: ct));
            await ServiceDatabase.InsertServiceAuditEvent(c, tx, actor, "ReversalScheduled", r.RequestId, r.Reason, ct);
        }

        await tx.CommitAsync(ct);
        return new
        {
            id = r.RequestId,
            status = "Scheduled"
        };
    }

    public async Task QueueDueReversalPreparations(CancellationToken ct)
    {
        await using var c = await db.OpenServiceDatabaseConnection(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        var rows = await c.QueryAsync<ReversalPreparation>(new CommandDefinition(
            SqlScripts.ReadSqlScript("PostgreSql/Queries/Scheduling/ScheduleStore/GetScheduledReversals"),
            transaction: tx,
            cancellationToken: ct));
        foreach (var r in rows)
        {
            await ServiceDatabase.SaveDomainEventToOutbox(
                c,
                tx,
                DomainEvent.CreateDomainEvent("scheduling", "reversal.preparation.requested", r, r.RequestId),
                ct);
            await c.ExecuteAsync(new CommandDefinition(
                """
                UPDATE scheduling."reversals"
                SET
                    status = 'Dispatched'
                WHERE "publicId" = @RequestId
                """,
                r,
                tx,
                cancellationToken: ct));
        }

        await tx.CommitAsync(ct);
    }

    public async Task<object> GetScheduledReversalReviews(CancellationToken ct)
    {
        await using var c = await db.OpenServiceDatabaseConnection(ct);
        return await c.QueryAsync(new CommandDefinition(
            SqlScripts.ReadSqlScript("PostgreSql/Queries/Scheduling/ScheduleStore/GetLatestReversals"),
            cancellationToken: ct));
    }
}
