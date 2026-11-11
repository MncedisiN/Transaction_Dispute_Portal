using DisputePortal.Api.Repository.Interfaces;
using Dapper;
using DisputePortal.Api.Models.Exceptions;
using Xunit;

namespace DisputePortal.IntegrationTests;

public sealed partial class DisputeWorkflowRepositoryTests
{
    private async Task<LegacySeedFixtureRow[]> InsertLegacySeedRegenerationFixtures(bool transactions = true, bool mismatchedOwner = false)
    {
        var first = Guid.Parse("10000000-0000-0000-0000-000000000001");
        var other = Guid.Parse("10000000-0000-0000-0000-000000000003");
        var rows = new[]
        {
            new LegacySeedFixtureRow
            {
                AccountId = Guid.Parse("20000000-0000-0000-0000-000000000001"),
                OwnerId = first,
                AccountType = "Cheque",
                Reference = "TX-DEMO-0001",
                TransactionId = Guid.NewGuid()
            },
            new LegacySeedFixtureRow
            {
                AccountId = Guid.Parse("20000000-0000-0000-0000-000000000002"),
                OwnerId = mismatchedOwner ? OtherCustomer : first,
                AccountType = "Savings",
                Reference = "TX-DEMO-0002",
                TransactionId = Guid.NewGuid()
            },
            new LegacySeedFixtureRow
            {
                AccountId = Guid.Parse("20000000-0000-0000-0000-000000000003"),
                OwnerId = other,
                AccountType = "Cheque",
                Reference = "TX-OTHER-0001",
                TransactionId = Guid.NewGuid()
            }
        };
        await using var c = await _connections.OpenConnection(CancellationToken.None);
        await c.ExecuteAsync(
            """
            INSERT INTO "identity"."users"("publicId",
                "loginIdentifier",
                "normalizedLoginIdentifier",
                "passwordHash")
            VALUES(@First,
                'legacy.first@example.test',
                'LEGACY.FIRST@EXAMPLE.TEST',
                'not-used'),
            (@Other,
                'legacy.other@example.test',
                'LEGACY.OTHER@EXAMPLE.TEST',
                'not-used');
            INSERT INTO "identity"."customerProfiles"("userId",
                "displayName",
                "maskedEmail",
                "maskedPhone")
            VALUES((
                    SELECT "resolved1"."id"
                    FROM "identity"."users" AS "resolved1"
                    WHERE "resolved1"."publicId" = @First),
                'Legacy First Fixture',
                'l•••@example.test',
                'Not supplied'),
            ((
                    SELECT "resolved2"."id"
                    FROM "identity"."users" AS "resolved2"
                    WHERE "resolved2"."publicId" = @Other),
                'Legacy Other Fixture',
                'l•••@example.test',
                'Not supplied');
            INSERT INTO "identity"."userRoles"("userId",
                "roleId")
            SELECT(
                SELECT "resolved3"."id"
                FROM "identity"."users" AS "resolved3"
                WHERE "resolved3"."publicId" = @First),
            "roles"."id"
            FROM "identity"."roles" AS "roles"
            WHERE "roles"."code" = 'customer';
            INSERT INTO "identity"."userRoles"("userId",
                "roleId")
            SELECT(
                SELECT "resolved4"."id"
                FROM "identity"."users" AS "resolved4"
                WHERE "resolved4"."publicId" = @Other),
            "roles"."id"
            FROM "identity"."roles" AS "roles"
            WHERE "roles"."code" = 'customer';
            INSERT INTO "identity"."userRoles"("userId",
                "roleId")
            SELECT(
                SELECT "resolved5"."id"
                FROM "identity"."users" AS "resolved5"
                WHERE "resolved5"."publicId" = @Admin),
            "roles"."id"
            FROM "identity"."roles" AS "roles"
            WHERE "roles"."code" = 'super_admin';
            """,
            new
            {
                First = first,
                Other = other,
                Admin = OtherCustomer
            });
        foreach (var row in rows)
        {
            await c.ExecuteAsync(
                """
                INSERT INTO "accounts"."accounts"("publicId",
                    "userId",
                    "accountType",
                    "currencyCode",
                    "maskedAccountNumber")
                VALUES(@AccountId,
                    (
                        SELECT "resolved1"."id"
                        FROM "identity"."users" AS "resolved1"
                        WHERE "resolved1"."publicId" = @OwnerId),
                    @AccountType,
                    'ZAR',
                    '••••1234')
                """,
                row);
            if (transactions)
            {
                await c.ExecuteAsync(
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
                    VALUES(@Id,
                        (
                            SELECT "resolved1"."id"
                            FROM "accounts"."accounts" AS "resolved1"
                            WHERE "resolved1"."publicId" = @Account),
                        @Reference,
                        @Now,
                        'Legacy fixture purchase',
                        'Fixture Shop',
                        100,
                        'ZAR',
                        (
                            SELECT "resolved2"."id"
                            FROM "configuration"."transactionTypes" AS "resolved2"
                            WHERE "resolved2"."code" = 'CardPurchase'),
                        'Debit',
                        'Posted',
                        @Eligible)
                    """,
                    new
                    {
                        Id = row.TransactionId,
                        Account = row.AccountId,
                        row.Reference,
                        Now = DateTimeOffset.UtcNow.AddDays(-1),
                        Eligible = true
                    });
            }
        }

        return rows;
    }

    [Fact]
    public async Task Legacy_seed_regeneration_adopts_only_known_fixture_pairs_and_preserves_case_and_journal_history()
    {
        var ct = CancellationToken.None;
        var rows = await InsertLegacySeedRegenerationFixtures();
        await using var c = await _connections.OpenConnection(ct);
        var linked = Guid.NewGuid();
        var history = Guid.NewGuid();
        await c.ExecuteAsync(
            """
            INSERT INTO "disputes"."disputes"("publicId",
                "reference",
                "transactionId",
                "customerId",
                "categoryId",
                "reason",
                "description",
                "status",
                "priority",
                "createdAt",
                "updatedAt")
            VALUES(@Case,
                'DP-LEGACY-REGEN-1',
                (
                    SELECT "resolved1"."id"
                    FROM "transactions"."transactions" AS "resolved1"
                    WHERE "resolved1"."publicId" = @Transaction),
                (
                    SELECT "resolved2"."id"
                    FROM "identity"."users" AS "resolved2"
                    WHERE "resolved2"."publicId" = @Owner),
                (
                    SELECT "resolved3"."id"
                    FROM "configuration"."disputeCategories" AS "resolved3"
                    WHERE "resolved3"."code" = 'Other'),
                'Review legacy purchase',
                'Preserve this existing fixture investigation.',
                'Submitted',
                3,
                @Now,
                @Now);
            INSERT INTO "disputes"."statusHistory"("publicId",
                "disputeId",
                "previousStatus",
                "newStatus",
                "actorUserId",
                "changedAt",
                "comment")
            VALUES(@History,
                (
                    SELECT "resolved4"."id"
                    FROM "disputes"."disputes" AS "resolved4"
                    WHERE "resolved4"."publicId" = @Case),
                NULL,
                'Submitted',
                (
                    SELECT "resolved5"."id"
                    FROM "identity"."users" AS "resolved5"
                    WHERE "resolved5"."publicId" = @Owner),
                @Now,
                'Original legacy fixture history.');
            """,
            new
            {
                Case = linked,
                Transaction = rows[0].TransactionId,
                Owner = rows[0].OwnerId,
                History = history,
                Now = DateTimeOffset.UtcNow
            });
        var journalBefore = await c.ExecuteScalarAsync<int>("""
            SELECT COUNT(*)
            FROM "ledger"."projectionEntries" AS "financial_ledger"
            """);
        var targets = await _repository.GetRegenerationTargets(OtherCustomer, ct);
        Assert.All(rows, row => Assert.Contains(targets, target => target.AccountId == row.AccountId));
        Assert.DoesNotContain(targets, target => target.AccountId == Account || target.AccountId == OtherAccount);
        var request = new RegenerateBankingRequest
        {
            RequestId = Guid.NewGuid(),
            AccountId = null,
            CountPerAccount = 30,
            Reason = "Regenerate the known legacy assessment fixture accounts."
        };
        var result = await _repository.RegenerateTransactions(OtherCustomer, request, ct);
        Assert.Equal(3, result.AccountCount);
        Assert.Equal(3, result.ArchivedCount);
        Assert.Equal(93, result.CreatedCount);
        var adopted = await c.ExecuteScalarAsync<int>("""
            SELECT COUNT(*)
            FROM "audit"."auditEvents" AS "admin_audit_logs"
            WHERE "admin_audit_logs"."action" = 'LocalBankingFixtureAdopted'
            """);
        Assert.Equal(3, adopted);
        Assert.Equivalent(result, await _repository.RegenerateTransactions(OtherCustomer, request, ct), strict: true);
        Assert.Equal(
            adopted,
            await c.ExecuteScalarAsync<int>("""
            SELECT COUNT(*)
            FROM "audit"."auditEvents" AS "admin_audit_logs"
            WHERE "admin_audit_logs"."action" = 'LocalBankingFixtureAdopted'
            """));
        foreach (var row in rows)
        {
            Assert.Equal(
                1,
                await c.ExecuteScalarAsync<int>(
                    """
                SELECT COUNT(*)
                FROM "integration"."localBankingAccounts" AS "demo_bank_accounts"
                WHERE(
                    SELECT "related1"."publicId"
                    FROM "accounts"."accounts" AS "related1"
                    WHERE "related1"."id" = "demo_bank_accounts"."accountId") = @Account
                AND(
                    SELECT(
                        SELECT "related3"."publicId"
                        FROM "identity"."users" AS "related3"
                        WHERE "related3"."id" = "related2"."userId")
                    FROM "integration"."localBankingIdentities" AS "related2"
                    WHERE "related2"."id" = "demo_bank_accounts"."bankingIdentityId") = @Owner
                """,
                    new { Account = row.AccountId, Owner = row.OwnerId }));
            Assert.Equal(
                31,
                await c.ExecuteScalarAsync<int>(
                    """
                SELECT COUNT(*)
                FROM "transactions"."transactions" AS "transactions"
                WHERE(
                    SELECT "related1"."publicId"
                    FROM "accounts"."accounts" AS "related1"
                    WHERE "related1"."id" = "transactions"."accountId") = @Account
                AND "transactions"."archivedAt" IS NULL
                """,
                    new { Account = row.AccountId }));
            Assert.Equal(
                1,
                await c.ExecuteScalarAsync<int>(
                    """
                SELECT COUNT(*)
                FROM "transactions"."transactions" AS "transactions"
                WHERE "transactions"."publicId" = @Id
                AND "transactions"."archivedAt" IS NOT NULL
                AND "transactions"."transactionStatus" = 'Posted'
                """,
                    new { Id = row.TransactionId }));
            Assert.True(await c.ExecuteScalarAsync<decimal>(
                """
                SELECT COALESCE(SUM("financial_ledger"."currentDelta"),
                    0) - COALESCE(SUM("financial_ledger"."reservedDelta"),
                    0)
                FROM "ledger"."projectionEntries" AS "financial_ledger"
                WHERE(
                    SELECT "related1"."publicId"
                    FROM "accounts"."accounts" AS "related1"
                    WHERE "related1"."id" = "financial_ledger"."accountId") = @Account
                """,
                new { Account = row.AccountId }) > 0);
            Assert.Equal(
                0m,
                await c.ExecuteScalarAsync<decimal>(
                    """
                SELECT COALESCE(SUM("financial_ledger"."currentDelta"),
                    0)
                FROM "ledger"."projectionEntries" AS "financial_ledger"
                WHERE(
                    SELECT "related1"."publicId"
                    FROM "transactions"."transactions" AS "related1"
                    WHERE "related1"."id" = "financial_ledger"."transactionId") = @Id
                """,
                    new { Id = row.TransactionId }));
        }

        Assert.True(await c.ExecuteScalarAsync<int>("""
            SELECT COUNT(*)
            FROM "ledger"."projectionEntries" AS "financial_ledger"
            """) > journalBefore);
        Assert.Equal(
            1,
            await c.ExecuteScalarAsync<int>(
                """
            SELECT COUNT(*)
            FROM "disputes"."disputes" AS "transaction_disputes"
            WHERE "transaction_disputes"."publicId" = @Case
            AND(
                SELECT "related1"."publicId"
                FROM "transactions"."transactions" AS "related1"
                WHERE "related1"."id" = "transaction_disputes"."transactionId") = @Transaction
            """,
                new { Case = linked, Transaction = rows[0].TransactionId }));
        Assert.Equal(
            "Original legacy fixture history.",
            await c.ExecuteScalarAsync<string>(
                """
            SELECT "dispute_status_history"."comment" AS "comment"
            FROM "disputes"."statusHistory" AS "dispute_status_history"
            WHERE "dispute_status_history"."publicId" = @Id
            """,
                new { Id = history }));
        Assert.NotNull(await _repository.GetCustomerDispute(rows[0].OwnerId, linked, ct));
    }

    [Fact]
    public async Task Legacy_seed_regeneration_rejects_changed_owners_external_links_and_arbitrary_unlinked_accounts()
    {
        var ct = CancellationToken.None;
        var rows = await InsertLegacySeedRegenerationFixtures(false, true);
        await using var c = await _connections.OpenConnection(ct);
        // The UUID alone is insufficient: its original fixture owner must also match.
        await _repository.LinkOwnedAccount(rows[2].OwnerId, rows[2].AccountId, "fixture-external", "owned-external-fixture", ct);
        var targets = await _repository.GetRegenerationTargets(OtherCustomer, ct);
        Assert.Contains(targets, target => target.AccountId == rows[0].AccountId);
        Assert.DoesNotContain(
            targets,
            target => target.AccountId == rows[1].AccountId || target.AccountId == rows[2].AccountId || target.AccountId == Account);
        foreach (var excluded in new[]
        {
            rows[1].AccountId,
            rows[2].AccountId,
            Account
        }

        )
            await Assert.ThrowsAsync<PortalNotFoundException>(() => _repository.RegenerateTransactions(
            OtherCustomer,
            new global::DisputePortal.Api.Repository.Interfaces.RegenerateBankingRequest
            {
                RequestId = Guid.NewGuid(),
                AccountId = excluded,
                CountPerAccount = 30,
                Reason = "This account must not be adopted for regeneration."
            },
            ct));
        Assert.Equal(
            0,
            await c.ExecuteScalarAsync<int>("""
            SELECT COUNT(*)
            FROM "integration"."localBankingAccounts" AS "demo_bank_accounts"
            """));
        Assert.Equal(
            0,
            await c.ExecuteScalarAsync<int>("""
            SELECT COUNT(*)
            FROM "audit"."auditEvents" AS "admin_audit_logs"
            WHERE "admin_audit_logs"."action" = 'LocalBankingFixtureAdopted'
            """));
    }

    private class LegacySeedFixtureRow
    {
        public Guid AccountId { get; set; }
        public Guid OwnerId { get; set; }
        public string AccountType { get; set; } = null!;
        public string Reference { get; set; } = null!;
        public Guid TransactionId { get; set; }
    }
}
