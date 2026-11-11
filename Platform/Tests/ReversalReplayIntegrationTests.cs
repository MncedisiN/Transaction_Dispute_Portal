using Dapper;
using DisputePortal.Platform.Contracts;
using DisputePortal.Platform.ReversalReviews;
using DisputePortal.Platform.Infrastructure;
using Xunit;

namespace DisputePortal.Platform.Tests;

public partial class ServiceDomainMigrationTests
{
    [NativePostgreSqlFact]
    public async Task Reversal_republication_preserves_terms_and_rejects_conflicting_requests()
    {
        await using var fixture = await DatabaseFixture.Create("debt");
        var database = fixture.Database;
        await database.ApplyServiceSchemaMigrations(
            ReversalReviewStore.Schema,
            ServiceMigrations.GetServiceMigrations("debt", "PostgreSql"));
        var store = new ReversalReviewStore(database);
        var request = new ReversalPreparation
        {
            RequestId = Guid.NewGuid(),
            OriginalTransactionId = Guid.NewGuid(),
            ActorId = Guid.NewGuid(),
            Kind = "Refund"
        };
        DomainEvent Republish(ReversalPreparation value) => DomainEvent.CreateDomainEvent(
            "scheduling",
            "reversal.preparation.requested",
            value,
            request.RequestId);

        await store.SaveReversalReviewRequest(Republish(request), default);
        await store.SaveReversalReviewRequest(Republish(request), default);
        var conflicting = new ReversalPreparation
        {
            RequestId = request.RequestId,
            OriginalTransactionId = request.OriginalTransactionId,
            ActorId = request.ActorId,
            Kind = "Recall"
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveReversalReviewRequest(Republish(conflicting), default));
        await using var connection = await database.OpenServiceDatabaseConnection();
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM debt.\"reversalReviews\""));
        Assert.Equal(2, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM service_inbox"));
        var review = await store.ClaimNextReversalReview(default);
        Assert.NotNull(review);
        Assert.Equal("Refund", review.Request.Kind);
        Assert.Equal(request.OriginalTransactionId, review.Request.OriginalTransactionId);
    }
}
