using Dapper;
using DisputePortal.Api.Models.Domain;
using DisputePortal.Api.Models.Internal;
using DisputePortal.Api.Repository.Data;
using Xunit;

namespace DisputePortal.IntegrationTests;

public sealed partial class DisputeWorkflowRepositoryTests
{
    [Fact]
    public async Task Customer_history_uses_the_configured_account_opening_window_without_changing_transactions()
    {
        await using var connection = await _connections.OpenConnection(default);
        var opened = DateTimeOffset.UtcNow.AddDays(-40);
        await connection.ExecuteAsync("""
            UPDATE accounts."accounts"
            SET "createdAt" = @Opened
            WHERE "publicId" = @Account
            """, new { Opened = opened, Account });
        var inside = Guid.NewGuid();
        var boundary = Guid.NewGuid();
        var beforeOpening = Guid.NewGuid();
        const string insert = """
            INSERT INTO transactions."transactions" (
                "publicId",
                "accountId",
                "transactionReference",
                "transactionAt",
                "description",
                "merchantName",
                "amount",
                "currencyCode",
                "transactionTypeId",
                "direction",
                "transactionStatus",
                "disputeEligible"
            )
            SELECT
                @Id,
                "accountId",
                @Reference,
                @At,
                "description",
                "merchantName",
                "amount",
                "currencyCode",
                "transactionTypeId",
                "direction",
                "transactionStatus",
                "disputeEligible"
            FROM transactions."transactions"
            WHERE "publicId" = @Original
            """;
        foreach (var entry in new[] { (inside, opened.AddDays(29)), (boundary, opened.AddDays(30)), (beforeOpening, opened.AddDays(-1)) })
        {
            await connection.ExecuteAsync(insert, new { Id = entry.Item1, Reference = entry.Item1.ToString("N"), At = entry.Item2, Original = Transaction });
        }
        var original = await connection.QuerySingleAsync<(decimal Amount, string Status, DateTimeOffset At)>("""
            SELECT "amount", "transactionStatus" AS Status, "transactionAt" AS At
            FROM transactions."transactions"
            WHERE "publicId" = @Transaction
            """, new { Transaction });
        var first = await _repository.GetTransactions(Customer, new TransactionFilter { AccountId = Account, Page = 1, PageSize = 10 }, default);
        Assert.Equal(1, first.Total);
        Assert.Equal(inside, Assert.Single(first.Items).Id);
        var update = _provider == DatabaseProvider.SqlServer
            ? "UPDATE configuration.\"portalSettings\" SET \"body\" = JSON_MODIFY(\"body\", '$.clientOptions.transactionHistoryDays', 60) WHERE \"code\" = 'Interface'"
            : "UPDATE configuration.\"portalSettings\" SET \"body\" = jsonb_set(\"body\"::jsonb, '{clientOptions,transactionHistoryDays}', '60'::jsonb) WHERE \"code\" = 'Interface'";
        await connection.ExecuteAsync(update);
        var expanded = await _repository.GetTransactions(Customer, new TransactionFilter { AccountId = Account, Page = 1, PageSize = 10 }, default);
        Assert.Equal(3, expanded.Total);
        Assert.DoesNotContain(expanded.Items, item => item.Id == beforeOpening);
        Assert.Equal(original, await connection.QuerySingleAsync<(decimal Amount, string Status, DateTimeOffset At)>("""
            SELECT "amount", "transactionStatus" AS Status, "transactionAt" AS At
            FROM transactions."transactions"
            WHERE "publicId" = @Transaction
            """, new { Transaction }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Case_history_keeps_active_cases_and_limits_closed_cases_without_deleting_details(bool closed)
    {
        var created = await _repository.CreateDispute(Customer, new CreateDisputeCommand
        {
            TransactionId = Transaction,
            Category = "Unrecognized",
            Reason = "I did not make this transaction",
            Description = "Please review this payment and preserve its recorded outcome."
        }, "history-window-" + Guid.NewGuid().ToString("N"), default);
        var detail = await _repository.GetCustomerDispute(Customer, created.Id, default);
        const string resolution = "The merchant evidence confirms the payment was authorised; no refund approved.";
        if (closed)
        {
            var version = await _repository.TransitionDispute(Agent, created.Id,
                new TransitionCommand(detail!.Dispute.Version, "UnderReview", "Reviewing the bank and merchant evidence."), "history-window", default);
            await _repository.TransitionDispute(Agent, created.Id,
                new TransitionCommand(version, "Declined", resolution), "history-window", default);
        }
        await using var connection = await _connections.OpenConnection(default);
        await connection.ExecuteAsync("""
            UPDATE disputes."disputes"
            SET "createdAt" = @CreatedAt,
                "updatedAt" = @UpdatedAt,
                "resolvedAt" = @ResolvedAt
            WHERE "publicId" = @Id
            """, new { Id = created.Id, CreatedAt = DateTimeOffset.UtcNow.AddDays(-90), UpdatedAt = DateTimeOffset.UtcNow.AddDays(-31), ResolvedAt = closed ? DateTimeOffset.UtcNow.AddDays(-31) : (DateTimeOffset?)null });
        var listed = await _repository.GetCustomerDisputes(Customer, 1, 10, default, Account, closed ? "closed" : "active");
        Assert.Equal(closed ? 0 : 1, listed.Total);
        var retained = await _repository.GetCustomerDispute(Customer, created.Id, default);
        Assert.NotNull(retained);
        if (closed) Assert.Equal(resolution, retained.Dispute.Resolution);
    }
}
