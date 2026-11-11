using Dapper;
using DisputePortal.Api.Models.Exceptions;
using DisputePortal.Api.Repository;
using DisputePortal.Api.Repository.Data;
using Xunit;

namespace DisputePortal.IntegrationTests;

public sealed partial class DisputeWorkflowRepositoryTests
{
    [Fact]
    public async Task Related_case_lookup_pages_recent_cases_and_finds_owned_historical_references_with_staff_scope()
    {
        var ct = CancellationToken.None;
        var now = DateTimeOffset.Parse("2026-11-02T12:00:00Z");
        var lookup = new SupportRepository(
            _connections,
            DatabaseDialect.GetDatabaseDialect(_provider),
            TimeZoneInfo.FindSystemTimeZoneById("Africa/Johannesburg"),
            new RelatedCaseClock(now));
        await using var connection = await _connections.OpenConnection(ct);
        async Task<Guid> AddCase(
            Guid owner,
            Guid account,
            string reference,
            DateTimeOffset created,
            string queue = "General",
            string status = "Submitted")
        {
            var transaction = Guid.NewGuid();
            var id = Guid.NewGuid();
            await connection.ExecuteAsync(
                """
                INSERT INTO "transactions"."transactions"("publicId",
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
                    "disputeEligible")
                VALUES(@Transaction,
                    (
                        SELECT "resolved1"."id"
                        FROM "accounts"."accounts" AS "resolved1"
                        WHERE "resolved1"."publicId" = @Account),
                    @Reference,
                    @Created,
                    'Lookup payment',
                    'Lookup Shop',
                    20.00,
                    'ZAR',
                    (
                        SELECT "resolved2"."id"
                        FROM "configuration"."transactionTypes" AS "resolved2"
                        WHERE "resolved2"."code" = 'CardPurchase'),
                    'Debit',
                    'Posted',
                    @Eligible);
                INSERT INTO "disputes"."disputes"("publicId",
                    "reference",
                    "transactionId",
                    "customerId",
                    "categoryId",
                    "reason",
                    "description",
                    "status",
                    "queueType",
                    "idempotencyKey",
                    "createdAt",
                    "updatedAt")
                VALUES(@Id,
                    @Reference,
                    (
                        SELECT "resolved3"."id"
                        FROM "transactions"."transactions" AS "resolved3"
                        WHERE "resolved3"."publicId" = @Transaction),
                    (
                        SELECT "resolved4"."id"
                        FROM "identity"."users" AS "resolved4"
                        WHERE "resolved4"."publicId" = @Owner),
                    (
                        SELECT "resolved5"."id"
                        FROM "configuration"."disputeCategories" AS "resolved5"
                        WHERE "resolved5"."code" = 'Other'),
                    'Lookup fixture',
                    'Historical lookup record',
                    @Status,
                    @Queue,
                    @Reference,
                    @Created,
                    @Created);
                """,
                new
                {
                    Transaction = transaction,
                    Account = account,
                    Reference = reference,
                    Created = created,
                    Eligible = true,
                    Id = id,
                    Owner = owner,
                    Status = status,
                    Queue = queue
                });
            return id;
        }

        var recent = await AddCase(Customer, Account, "DP-LOOKUP-RECENT", now.AddDays(-1));
        var boundary = await AddCase(Customer, Account, "DP-LOOKUP-BOUNDARY", now.AddDays(-30));
        var historical = await AddCase(Customer, Account, "DP-LOOKUP-OLD%_[]", now.AddDays(-31), status: "Declined");
        var future = await AddCase(Customer, Account, "DP-LOOKUP-FUTURE", now.AddDays(1));
        var fraud = await AddCase(Customer, Account, "DP-LOOKUP-FRAUD", now.AddDays(-2), "Fraud");
        var card = await AddCase(Customer, Account, "DP-LOOKUP-CARD", now.AddDays(-3), "Card");
        var other = await AddCase(OtherCustomer, OtherAccount, "DP-LOOKUP-OTHER", now.AddDays(-1));
        var note = Guid.NewGuid();
        await connection.ExecuteAsync(
            """
            INSERT INTO "disputes"."notes"("publicId",
                "disputeId",
                "authorUserId",
                "content",
                "visibility")
            VALUES(@Id,
                (
                    SELECT "resolved1"."id"
                    FROM "disputes"."disputes" AS "resolved1"
                    WHERE "resolved1"."publicId" = @Case),
                (
                    SELECT "resolved2"."id"
                    FROM "identity"."users" AS "resolved2"
                    WHERE "resolved2"."publicId" = @Actor),
                'Original historical note',
                'Customer')
            """,
            new
            {
                Id = note,
                Case = historical,
                Actor = Customer
            });
        var first = await lookup.GetRelatedCases(Customer, 1, 2, null, ct);
        Assert.Equal(5, first.Total);
        Assert.Equal(new[] { recent, fraud }, first.Items.Select(item => item.Id));
        var second = await lookup.GetRelatedCases(Customer, 2, 2, "  ", ct);
        Assert.Equal(new[] { card, boundary }, second.Items.Select(item => item.Id));
        Assert.DoesNotContain(first.Items.Concat(second.Items), item => item.Id == historical || item.Id == future || item.Id == other);
        var last = await lookup.GetRelatedCases(Customer, 3, 2, null, ct);
        Assert.Equal(historical, Assert.Single(last.Items).Id);
        var older = await lookup.GetRelatedCases(Customer, 1, 20, "old%_[]", ct);
        Assert.Equal(historical, Assert.Single(older.Items).Id);
        Assert.Equal("Declined", older.Items[0].Status);
        Assert.Equal(historical, Assert.Single((await lookup.GetRelatedCases(Customer, 1, 20, "%", ct)).Items).Id);
        Assert.Empty((await lookup.GetRelatedCases(Customer, 1, 20, "Lookup Shop", ct)).Items);
        Assert.Empty((await lookup.GetRelatedCases(Customer, 1, 20, "DP-LOOKUP-OTHER", ct)).Items);
        Assert.Empty((await lookup.GetRelatedCases(Guid.Empty, 1, 20, "DP-LOOKUP", ct)).Items);
        Assert.Equal(50, (await lookup.GetRelatedCases(Customer, 1, 500, null, ct)).PageSize);
        Assert.Empty((await lookup.GetRelatedCases(Customer, int.MaxValue, 50, null, ct)).Items);
        await Assert.ThrowsAsync<PortalValidationException>(() => lookup.GetRelatedCases(Customer, 1, 20, new string('x', 101), ct));
        Assert.Equal(fraud, Assert.Single((await lookup.GetRelatedCases(Agent, 1, 20, null, ct)).Items).Id);
        Assert.Empty((await lookup.GetRelatedCases(Agent, 1, 20, "DP-LOOKUP-OLD", ct)).Items);
        await connection.ExecuteAsync(
            """
            INSERT INTO "identity"."userRoles"("userId",
                "roleId")
            SELECT(
                SELECT "resolved1"."id"
                FROM "identity"."users" AS "resolved1"
                WHERE "resolved1"."publicId" = @Actor),
            "roles"."id"
            FROM "identity"."roles" AS "roles"
            WHERE "roles"."code" = 'super_admin'
            """,
            new { Actor = OtherCustomer });
        Assert.Equal(6, (await lookup.GetRelatedCases(OtherCustomer, 1, 20, null, ct)).Total);
        Assert.Equal(historical, Assert.Single((await lookup.GetRelatedCases(OtherCustomer, 1, 20, "DP-LOOKUP-OLD", ct)).Items).Id);
        await connection.ExecuteAsync(
            """
            UPDATE "identity"."users"
            SET "accountStatus" = 'Disabled'
            WHERE "users"."publicId" = @Actor
            """,
            new { Actor = Customer });
        Assert.Empty((await lookup.GetRelatedCases(Customer, 1, 20, "DP-LOOKUP", ct)).Items);
        Assert.Equal(
            "Original historical note",
            await connection.ExecuteScalarAsync<string>(
                """
            SELECT "dispute_notes"."content" AS "content"
            FROM "disputes"."notes" AS "dispute_notes"
            WHERE "dispute_notes"."publicId" = @Id
            """,
                new { Id = note }));
        Assert.Equal(
            7,
            await connection.ExecuteScalarAsync<int>("""
            SELECT COUNT(*)
            FROM "disputes"."disputes" AS "transaction_disputes"
            """));
    }

    private sealed class RelatedCaseClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
