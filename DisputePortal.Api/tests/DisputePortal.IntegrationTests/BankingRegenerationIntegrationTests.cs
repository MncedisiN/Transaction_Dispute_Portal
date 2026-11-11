using DisputePortal.Api.Models.Domain;
using DisputePortal.Api.Repository.Interfaces;
using Dapper;
using DisputePortal.Api.Models.Exceptions;
using DisputePortal.Api.Models.Internal;
using DisputePortal.Api.Service.Disputes;
using Xunit;

namespace DisputePortal.IntegrationTests;

public sealed partial class DisputeWorkflowRepositoryTests
{
    [Fact]
    public async Task Administrator_regeneration_is_atomic_idempotent_and_preserves_archived_case_and_ledger_history()
    {
        var ct = CancellationToken.None;
        await VerifyProvisionedContact(Customer, "regeneration.owner@example.test");
        var account = await _repository.OpenAccount(Customer, Guid.NewGuid(), "Savings", 10, ct);
        Assert.True(await _repository.ProcessNextTransactionBatch(ct));
        await using var c = await _connections.OpenConnection(ct);
        var original = await c.ExecuteScalarAsync<Guid>(
            """
            SELECT(
                SELECT "related1"."publicId"
                FROM "transactions"."transactions" AS "related1"
                WHERE "related1"."id" = "transaction_duplicate_confirmations"."transactionId") AS "transaction_id"
            FROM "transactions"."duplicateConfirmations" AS "transaction_duplicate_confirmations"
            WHERE(
                SELECT "related2"."publicId"
                FROM "transactions"."transactions" AS "related2"
                WHERE "related2"."id" = "transaction_duplicate_confirmations"."transactionId") IN(
                SELECT "transactions"."publicId" AS "id"
                FROM "transactions"."transactions" AS "transactions"
                WHERE(
                    SELECT "related3"."publicId"
                    FROM "accounts"."accounts" AS "related3"
                    WHERE "related3"."id" = "transactions"."accountId") = @Account)
            """,
            new { Account = account.AccountId });
        var linked = await _repository.CreateDispute(
            Customer,
            new CreateDisputeCommand
            {
                TransactionId = original,
                Category = "Duplicate",
                Reason = "Repeated purchase",
                Description = "Please review this generated duplicate purchase."
            },
            "regen-case-" + Guid.NewGuid(),
            ct);
        var previousLedger = await c.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM "ledger"."projectionEntries" AS "financial_ledger"
            WHERE(
                SELECT "related1"."publicId"
                FROM "accounts"."accounts" AS "related1"
                WHERE "related1"."id" = "financial_ledger"."accountId") = @Account
            """,
            new { Account = account.AccountId });
        var before = await c.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM "transactions"."transactions" AS "transactions"
            WHERE(
                SELECT "related1"."publicId"
                FROM "accounts"."accounts" AS "related1"
                WHERE "related1"."id" = "transactions"."accountId") = @Account
            """,
            new { Account = account.AccountId });
        var request = new RegenerateBankingRequest
        {
            RequestId = Guid.NewGuid(),
            AccountId = account.AccountId,
            CountPerAccount = 30,
            Reason = "Refresh the local assessment transaction scenarios."
        };
        await Assert.ThrowsAsync<PortalForbiddenException>(() => _repository.RegenerateTransactions(Customer, request, ct));
        await c.ExecuteAsync(
            """
            INSERT INTO "identity"."userRoles"("userId",
                "roleId")
            SELECT(
                SELECT "resolved1"."id"
                FROM "identity"."users" AS "resolved1"
                WHERE "resolved1"."publicId" = @Admin),
            "roles"."id"
            FROM "identity"."roles" AS "roles"
            WHERE "roles"."code" = 'super_admin'
            """,
            new { Admin = OtherCustomer });
        var results = await Task.WhenAll(
            _repository.RegenerateTransactions(OtherCustomer, request, ct),
            _repository.RegenerateTransactions(OtherCustomer, request, ct));
        Assert.Equivalent(results[0], results[1], strict: true);
        Assert.Equal(1, results[0].AccountCount);
        Assert.Equal(before, results[0].ArchivedCount);
        Assert.Equal(31, results[0].CreatedCount);
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
                new { Account = account.AccountId }));
        Assert.Equal(
            before + 31,
            await c.ExecuteScalarAsync<int>(
                """
            SELECT COUNT(*)
            FROM "transactions"."transactions" AS "transactions"
            WHERE(
                SELECT "related1"."publicId"
                FROM "accounts"."accounts" AS "related1"
                WHERE "related1"."id" = "transactions"."accountId") = @Account
            """,
                new { Account = account.AccountId }));
        Assert.True(await c.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM "ledger"."projectionEntries" AS "financial_ledger"
            WHERE(
                SELECT "related1"."publicId"
                FROM "accounts"."accounts" AS "related1"
                WHERE "related1"."id" = "financial_ledger"."accountId") = @Account
            """,
            new { Account = account.AccountId }) > previousLedger);
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
            new { Account = account.AccountId }) > 0);
        Assert.NotNull(await _repository.GetCustomerDispute(Customer, linked.Id, ct));
        Assert.NotNull(await _repository.GetTransaction(Customer, original, ct));
        var feed = await _repository.GetTransactions(
            Customer,
            new TransactionFilter
            {
                Page = 1,
                PageSize = 100,
                Search = null,
                From = null,
                To = null,
                Direction = null,
                Type = null,
                Sort = "date_desc",
                AccountId = account.AccountId
            },
            ct);
        Assert.Equal(31, feed.Total);
        Assert.DoesNotContain(feed.Items, item => item.Id == original);
        await Assert.ThrowsAsync<PortalConflictException>(() => _repository.RegenerateTransactions(
            OtherCustomer,
            new global::DisputePortal.Api.Repository.Interfaces.RegenerateBankingRequest(request) { CountPerAccount = 40 },
            ct));
        Assert.Equal(
            1,
            await c.ExecuteScalarAsync<int>(
                """
            SELECT COUNT(*)
            FROM "integration"."bankingRegenerations" AS "local_banking_regenerations"
            WHERE "local_banking_regenerations"."publicId" = @Id
            """,
                new { Id = request.RequestId }));
    }
}
