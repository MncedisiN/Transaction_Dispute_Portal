using System.Text.Json;
using Dapper;
using DisputePortal.Platform.Contracts;
using DisputePortal.Platform.Infrastructure;
using DisputePortal.Platform.Scheduling;
using Xunit;

namespace DisputePortal.Platform.Tests;

public partial class ServiceDomainMigrationTests
{
    [NativePostgreSqlTheory]
    [InlineData("Ineligible", "already_refunded")]
    [InlineData("Failed", "insufficient_funds")]
    [InlineData("Completed", null)]
    public async Task Scheduled_review_persists_one_explicit_outcome_and_one_requestor_event(string status, string? code)
    {
        await using var fixture = await DatabaseFixture.Create("scheduling");
        var db = fixture.Database;
        await db.ApplyServiceSchemaMigrations(ScheduleStore.Schema, ServiceMigrations.GetServiceMigrations("scheduling", "PostgreSql"));
        var id = Guid.NewGuid();
        var actor = Guid.NewGuid();
        var original = Guid.NewGuid();
        await using var connection = await db.OpenServiceDatabaseConnection();
        await connection.ExecuteAsync(
            """
            INSERT INTO scheduling.reversals("publicId", "originalTransactionPublicId", kind, "actorPublicId", "dueAt", reason, status)
            VALUES(@Id, @Original, 'Refund', @Actor, now(), 'Review merchant evidence', 'Dispatched')
            """,
            new
            {
                Id = id,
                Original = original,
                Actor = actor
            });
        var value = status == "Ineligible" ? DomainEvent.CreateDomainEvent(
            "debt",
            "debt.reviewed",
            new
            {
                requestId = id,
                status,
                code
            },
            id) : DomainEvent.CreateDomainEvent(
            "transactions",
            status == "Completed" ? EventTypes.TransactionCompleted : EventTypes.TransactionFailed,
            new TransactionEvent
            {
                TransactionId = id,
                PaymentId = Guid.NewGuid(),
                UserId = actor,
                Kind = "Refund",
                Reference = "RF-REVIEW",
                AccountId = Guid.NewGuid(),
                RecipientAccountId = null,
                Amount = 100m,
                Currency = "ZAR",
                TargetAmount = null,
                TargetCurrency = null,
                Fee = 0m,
                Status = status,
                FailureCode = code
            },
            id);
        var consumer = new ScheduledReversalOutcomeConsumer(db);
        await consumer.Handle(value, default);
        await consumer.Handle(value, default);
        // Even a second envelope for the same terminal result cannot create another intent.
        await consumer.Handle(new global::DisputePortal.Platform.Contracts.DomainEvent(value) { EventId = Guid.NewGuid() }, default);
        Assert.Equal(
            status,
            await connection.ExecuteScalarAsync<string>("SELECT status FROM scheduling.reversals WHERE \"publicId\"=@Id", new
            {
                Id = id
            }));
        Assert.Equal(
            code,
            await connection.ExecuteScalarAsync<string?>("SELECT \"decisionCode\" FROM scheduling.reversals WHERE \"publicId\"=@Id", new
            {
                Id = id
            }));
        var body = await connection.QuerySingleAsync<string>("SELECT body::text FROM service_outbox WHERE event_type='reversal.review.completed'");
        var outcome = JsonSerializer.Deserialize<DomainEvent>(body, DomainEvent.Json)!.DeserializePayload<ReversalReviewOutcome>();
        Assert.Equal(actor, outcome.ActorId);
        Assert.Equal(original, outcome.OriginalTransactionId);
        Assert.Equal(status, outcome.Status);
        Assert.Equal(code, outcome.Code);
        Assert.Equal(
            1,
            await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM service_outbox WHERE event_type='reversal.review.completed'"));
    }
}
