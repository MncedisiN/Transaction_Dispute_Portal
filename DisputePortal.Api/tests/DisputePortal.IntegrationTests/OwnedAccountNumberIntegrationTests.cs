using Dapper;
using DisputePortal.Api.Configuration;
using DisputePortal.Api.Service.Security;
using DisputePortal.Api.Models.Exceptions;
using DisputePortal.Api.Models.Internal;
using DisputePortal.Api.Repository.Data;
using System.Text.Json;
using Xunit;

namespace DisputePortal.IntegrationTests;

public sealed partial class DisputeWorkflowRepositoryTests
{
    [Fact]
    public async Task Owned_account_reads_return_stable_persisted_numbers_without_fabricating_provider_details()
    {
        var ct = CancellationToken.None;
        await VerifyProvisionedContact(Customer, "account-numbers@example.test");
        await using var connection = await _connections.OpenConnection(ct);
        var providerAccount = Guid.NewGuid();
        var providerTransaction = Guid.NewGuid();
        await connection.ExecuteAsync(
            """
            INSERT INTO "integration"."localBankingIdentities"("userId",
                "createdAt")
            VALUES((
                    SELECT "resolved1"."id"
                    FROM "identity"."users" AS "resolved1"
                    WHERE "resolved1"."publicId" = @Customer),
                @Now);
            INSERT INTO "integration"."localBankingAccounts"("accountId",
                "bankingIdentityId",
                "openingRequestId",
                "initialCount")
            VALUES((
                    SELECT "resolved2"."id"
                    FROM "accounts"."accounts" AS "resolved2"
                    WHERE "resolved2"."publicId" = @Account),
                (
                    SELECT "resolved3"."id"
                    FROM "integration"."localBankingIdentities" AS "resolved3"
                    WHERE(
                        SELECT "related4"."publicId"
                        FROM "identity"."users" AS "related4"
                        WHERE "related4"."id" = "resolved3"."userId") = @Customer),
                @Opening,
                1);
            INSERT INTO "accounts"."accounts"("publicId",
                "userId",
                "accountType",
                "currencyCode",
                "maskedAccountNumber")
            VALUES(@Provider,
                (
                    SELECT "resolved5"."id"
                    FROM "identity"."users" AS "resolved5"
                    WHERE "resolved5"."publicId" = @Customer),
                'Savings',
                'ZAR',
                '••••1133');
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
            VALUES(@ProviderTransaction,
                (
                    SELECT "resolved6"."id"
                    FROM "accounts"."accounts" AS "resolved6"
                    WHERE "resolved6"."publicId" = @Provider),
                'PROVIDER-NUMBER-TEST',
                @Now,
                'Provider payment',
                'Provider merchant',
                12,
                'ZAR',
                (
                    SELECT "resolved7"."id"
                    FROM "configuration"."transactionTypes" AS "resolved7"
                    WHERE "resolved7"."code" = 'CardPurchase'),
                'Debit',
                'Posted',
                @Eligible);
            """,
            new
            {
                Account,
                Customer,
                Opening = Guid.NewGuid(),
                Provider = providerAccount,
                ProviderTransaction = providerTransaction,
                Now = DateTimeOffset.UtcNow,
                Eligible = true
            });
        await _repository.LinkOwnedAccount(Customer, providerAccount, "fixture-provider", "provider-account-reference", ct);
        // Ownership failure must not allocate or reveal the other customer's number.
        Assert.Null(await _repository.GetTransaction(OtherCustomer, Transaction, ct));
        Assert.Null(await _repository.GetAccountByPublicId(OtherCustomer, Account, ct));
        Assert.Null(await _repository.GetAccountByPublicId(Customer, Guid.NewGuid(), ct));
        Assert.Empty((await _repository.GetTransactions(
            OtherCustomer,
            new TransactionFilter
            {
                Page = 1,
                PageSize = 50,
                Search = null,
                From = null,
                To = null,
                Direction = null,
                Type = null,
                Sort = "date_desc",
                AccountId = Account
            },
            ct)).Items);
        Assert.DoesNotContain(await _repository.GetAccounts(OtherCustomer, ct), item => item.Id == Account);
        await Assert.ThrowsAsync<PortalNotFoundException>(() => _repository.RevealAccountNumber(OtherCustomer, Account, ct));
        Assert.Null(await connection.ExecuteScalarAsync<string>(
            """
            SELECT "customer_accounts"."protectedNumber" AS "protected_number"
            FROM "accounts"."accounts" AS "customer_accounts"
            WHERE "customer_accounts"."publicId" = @Account
            """,
            new { Account }));
        // Concurrent first reads reuse the existing local-only allocation under the shared lock.
        var accountRead = _repository.GetAccounts(Customer, ct);
        var transactionRead = _repository.GetTransaction(Customer, Transaction, ct);
        await Task.WhenAll(accountRead, transactionRead);
        var account = Assert.Single(await accountRead, item => item.Id == Account);
        var transaction = await transactionRead;
        Assert.NotNull(account.AccountNumber);
        Assert.Matches("^[0-9]{10}$", account.AccountNumber);
        Assert.EndsWith("0001", account.AccountNumber);
        Assert.Equal(account.AccountNumber, transaction!.AccountNumber);
        var detail = await _repository.GetAccountByPublicId(Customer, Account, ct);
        Assert.NotNull(detail);
        Assert.Equal(account.Id, detail.Id);
        Assert.Equal(account.AccountNumber, detail.AccountNumber);
        Assert.Equal(account.Currency, detail.Currency);
        Assert.Equal("Cheque", transaction.AccountType);
        var saved = await connection.QuerySingleAsync<AccountNumberState>(
            """
            SELECT "customer_accounts"."protectedNumber" AS ProtectedNumber,
            "customer_accounts"."numberHash" AS NumberHash
            FROM "accounts"."accounts" AS "customer_accounts"
            WHERE "customer_accounts"."publicId" = @Account
            """,
            new { Account });
        Assert.Equal(account.AccountNumber, new TestProtector().Unprotect(saved.ProtectedNumber!));
        Assert.Equal(
            new OtpHasher(new InfobipOptions { OtpHashKey = new string('t', 64) }).Hash(
            Guid.Empty,
            "account-number:" + account.AccountNumber),
            saved.NumberHash);
        var listed = await _repository.GetTransactions(
            Customer,
            new TransactionFilter
            {
                Page = 1,
                PageSize = 50,
                Search = null,
                From = null,
                To = null,
                Direction = null,
                Type = null,
                Sort = "date_desc",
                AccountId = Account
            },
            ct);
        Assert.Equal(account.AccountNumber, Assert.Single(listed.Items).AccountNumber);
        Assert.Equal("Cheque", Assert.Single(listed.Items).AccountType);
        Assert.Equal(account.AccountNumber, (await _repository.RevealAccountNumber(Customer, Account, ct)).AccountNumber);
        Assert.Equal(
            account.AccountNumber,
            Assert.Single((await _repository.GetProvisioningStatus(Customer, ct)).Accounts, item => item.AccountId == Account).AccountNumber);
        Assert.Equivalent(
            saved,
            await connection.QuerySingleAsync<AccountNumberState>(
                """
            SELECT "customer_accounts"."protectedNumber" AS ProtectedNumber,
            "customer_accounts"."numberHash" AS NumberHash
            FROM "accounts"."accounts" AS "customer_accounts"
            WHERE "customer_accounts"."publicId" = @Account
            """,
                new { Account }),
            strict: true);
        await Task.WhenAll(_repository.GetAccounts(Customer, ct), _repository.GetTransaction(Customer, Transaction, ct));
        var audit = Assert.Single(await connection.QueryAsync<AccountNumberAudit>(
            $$"""
            SELECT actor."publicId" AS ActorUserId,
                {{DatabaseDialect.GetDatabaseDialect(_provider).AuditMetadataSelectExpression("auditRecord.\"safeMetadata\"")}} AS Metadata
            FROM "audit"."auditEvents" AS auditRecord
            LEFT JOIN "identity"."users" AS actor
                ON actor."id" = auditRecord."actorUserId"
            WHERE auditRecord."entityPublicId" = @Account
                AND auditRecord."action" = 'LocalAccountNumberAssigned'
            """,
            new { Account }));
        Assert.Null(audit.ActorUserId);
        using (var metadata = JsonDocument.Parse(audit.Metadata))
        {
            Assert.Equal(
                new[] { "AccountId", "Source" },
                metadata.RootElement.EnumerateObject().Select(item => item.Name).Order().ToArray());
            Assert.Equal(Account, metadata.RootElement.GetProperty("AccountId").GetGuid());
            Assert.Equal("LocalAllocation", metadata.RootElement.GetProperty("Source").GetString());
        }

        // A linked provider account with only a suffix receives no fabricated full value.
        Assert.Null(Assert.Single(await _repository.GetAccounts(Customer, ct), item => item.Id == providerAccount).AccountNumber);
        Assert.Null((await _repository.GetAccountByPublicId(Customer, providerAccount, ct))!.AccountNumber);
        var unavailable = await _repository.GetTransaction(Customer, providerTransaction, ct);
        Assert.Equal(string.Empty, unavailable!.AccountNumber);
        Assert.Equal("Savings", unavailable.AccountType);
        Assert.Equal(
            string.Empty,
            Assert.Single((await _repository.GetProvisioningStatus(Customer, ct)).Accounts, item => item.AccountId == providerAccount).AccountNumber);
        await Assert.ThrowsAsync<PortalConflictException>(() => _repository.RevealAccountNumber(Customer, providerAccount, ct));
        Assert.Equivalent(
            new AccountNumberState
            {
                ProtectedNumber = null,
                NumberHash = null
            },
            await connection.QuerySingleAsync<AccountNumberState>(
                """
            SELECT "customer_accounts"."protectedNumber" AS ProtectedNumber,
            "customer_accounts"."numberHash" AS NumberHash
            FROM "accounts"."accounts" AS "customer_accounts"
            WHERE "customer_accounts"."publicId" = @Provider
            """,
                new { Provider = providerAccount }),
            strict: true);
        // Once a real value exists in the protected field, every owned read returns that exact value.
        const string supplied = "7654321133";
        await connection.ExecuteAsync(
            """
            UPDATE "accounts"."accounts"
            SET "protectedNumber" = @Protected
            WHERE "accounts"."publicId" = @Provider
            """,
            new { Protected = new TestProtector().Protect(supplied), Provider = providerAccount });
        Assert.Equal(supplied, Assert.Single(await _repository.GetAccounts(Customer, ct), item => item.Id == providerAccount).AccountNumber);
        Assert.Equal(supplied, (await _repository.GetTransaction(Customer, providerTransaction, ct))!.AccountNumber);
        Assert.Equal(
            supplied,
            Assert.Single((await _repository.GetProvisioningStatus(Customer, ct)).Accounts, item => item.AccountId == providerAccount).AccountNumber);
        Assert.Null(await _repository.GetTransaction(OtherCustomer, providerTransaction, ct));
        Assert.Equal(
            0,
            await connection.ExecuteScalarAsync<int>(
                """
            SELECT COUNT(*)
            FROM "audit"."auditEvents" AS "admin_audit_logs"
            WHERE "admin_audit_logs"."entityPublicId" = @Provider
            AND "admin_audit_logs"."action" = 'LocalAccountNumberAssigned'
            """,
                new { Provider = providerAccount }));
    }

    private class AccountNumberState
    {
        public string? ProtectedNumber { get; set; }
        public string? NumberHash { get; set; }
    }

    private class AccountNumberAudit
    {
        public Guid? ActorUserId { get; set; }
        public string Metadata { get; set; } = null!;
    }
}
