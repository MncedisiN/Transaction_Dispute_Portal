using DisputePortal.Api.Repository.Interfaces;
using Dapper;
using System.Security.Cryptography;
using DisputePortal.Api.Models.Exceptions;
using DisputePortal.Api.Models.Responses;
using System.Text;
using System.Data.Common;
using DisputePortal.Api.Models.Domain;
using DisputePortal.Api.Interfaces.Data;
using DisputePortal.Api.Interfaces.Security;
using DisputePortal.Api.Interfaces.Services;

namespace DisputePortal.Api.Repository;
// Reproduces prior generated records exclusively inside disposable integration databases.
public sealed partial class LegacyBankingFixture(
    IDatabaseConnectionFactory factory,
    IDatabaseDialect databaseDialect,
    ISecretProtector protector,
    IOtpHasher hasher,
    IRealtimePublisher? publisher = null) : BankingRepository(
        factory,
        databaseDialect,
        TimeZoneInfo.FindSystemTimeZoneById("Africa/Johannesburg"),
        protector,
        hasher,
        new PortalSettingsRepository(factory, databaseDialect),
        platform: new DisputePortal.IntegrationTests.CountryPolicyFixtureClient()), IBankingProvisioningRepository
{
    private readonly ISecretProtector secretProtector = protector;
    private readonly IOtpHasher otpHasher = hasher;
    private readonly IRealtimePublisher? realtime = publisher;

    private static string SourceHash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static DateTimeOffset ProvisioningNow()
    {
        var now = DateTimeOffset.UtcNow;
        return new DateTimeOffset(now.UtcTicks - now.UtcTicks % 10, TimeSpan.Zero);
    }

    private const string DemoBatchSelect = """
        SELECT "b"."publicId" AS "id",
        (
            SELECT(
                SELECT "related2"."publicId"
                FROM "accounts"."accounts" AS "related2"
                WHERE "related2"."id" = "related1"."accountId")
            FROM "integration"."localBankingAccounts" AS "related1"
            WHERE "related1"."id" = "b"."bankingAccountId") AS AccountId,
        "b"."transactionCount" AS COUNT,
        "b"."state" AS "state",
        "b"."attempts" AS "attempts",
        "b"."createdAt" AS CreatedAt,
        "b"."completedAt" AS CompletedAt
        FROM "integration"."localTransactionBatches" b
        """;

    private static void ValidateProvisionedRequest(Guid request, int count)
    {
        if (request == Guid.Empty || count is < 1 or > 50)
        {
            throw new PortalValidationException("Use a request reference and between 1 and 50 transactions.");
        }
    }

    private async Task<ProvisionedIdentityFacts> ProvisionedIdentity(DbConnection c, DbTransaction? tx, Guid customer, CancellationToken ct) =>
        await c.QuerySingleOrDefaultAsync<ProvisionedIdentityFacts>(new CommandDefinition(
        $$"""
            SELECT p."protectedIdentityNumber" AS ProtectedIdentity,
                CASE WHEN (p."emailVerified" = {{dialect.TrueLiteral}} AND p."email" IS NOT NULL)
                    OR (p."phoneVerified" = {{dialect.TrueLiteral}} AND p."phone" IS NOT NULL)
                    THEN 1 ELSE 0 END AS ContactVerified
            FROM "identity"."users" AS u
            JOIN "identity"."customerProfiles" AS p
                ON p."userId" = u."id"
            WHERE u."publicId" = @Customer
                AND u."accountStatus" = 'Active'
                AND EXISTS (
                    SELECT 1
                    FROM "identity"."userRoles" AS ur
                    JOIN "identity"."roles" AS r
                        ON r."id" = ur."roleId"
                    WHERE ur."userId" = u."id"
                        AND r."code" = 'customer')
            """,
        new { Customer = customer },
        tx,
        cancellationToken: ct)) ?? throw new PortalForbiddenException();

    public async Task<BankingProvisioningStatusDto> GetProvisioningStatus(Guid customer, CancellationToken ct)
    {
        await using var c = await connections.OpenConnection(ct);
        var facts = await ProvisionedIdentity(c, null, customer, ct);
        var accounts = (await c.QueryAsync<ProvisionedBankAccountDto>(new CommandDefinition(
            """
            SELECT "a"."publicId" AS AccountId,
            "a"."accountType" AS AccountType,
            (
                SELECT COUNT(*)
                FROM "transactions"."transactions" t
                WHERE "t"."accountId" = "a"."id"
                AND "t"."archivedAt" IS NULL) AS TransactionCount
            FROM "accounts"."accounts" a
            WHERE(
                SELECT "related1"."publicId"
                FROM "identity"."users" AS "related1"
                WHERE "related1"."id" = "a"."userId") = @Customer
            AND "a"."accountType" IN('Cheque',
                'Savings')
            AND "a"."accountStatus" <> 'Closed'
            ORDER BY "a"."accountType",
            "a"."publicId"
            """,
            new { Customer = customer },
            cancellationToken: ct))).AsList();
        var numbers = (await GetAccounts(customer, ct)).ToDictionary(a => a.Id, a => a.AccountNumber);
        foreach (var account in accounts)
            account.AccountNumber = numbers.GetValueOrDefault(account.AccountId) ?? string.Empty;
        var batches = (await c.QueryAsync<BankingBatchDto>(new CommandDefinition(
            DemoBatchSelect + """
            
            WHERE(
                SELECT "related1"."publicId"
                FROM "identity"."users" AS "related1"
                WHERE "related1"."id" = "b"."userId") = @Customer
            ORDER BY "b"."createdAt" DESC,
            "b"."publicId" DESC
            OFFSET 0 ROWS
            FETCH NEXT 100 ROWS ONLY
            """,
            new { Customer = customer },
            cancellationToken: ct))).AsList();
        foreach (var account in accounts)
            account.LastBatch = batches.FirstOrDefault(batch => batch.AccountId == account.AccountId);
        return new global::DisputePortal.Api.Models.Responses.BankingProvisioningStatusDto
        {
            IdentityRecorded = facts.ProtectedIdentity is not null,
            ContactVerified = facts.ContactVerified,
            AccountLimit = CustomerAccountPolicy.MaximumAccounts,
            Accounts = accounts
        };
    }

    private async Task LinkProvisionedIdentity(DbConnection c, DbTransaction tx, Guid customer, CancellationToken ct)
    {
        var facts = await ProvisionedIdentity(c, tx, customer, ct);
        if (!facts.ContactVerified)
        {
            throw new PortalConflictException("Verify your email or mobile in Profile before opening an account.");
        }

        var existing = await c.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT COUNT(*)
            FROM "integration"."localBankingIdentities" AS "demo_bank_identities"
            WHERE(
                SELECT "related1"."publicId"
                FROM "identity"."users" AS "related1"
                WHERE "related1"."id" = "demo_bank_identities"."userId") = @Customer
            """,
            new { Customer = customer },
            tx,
            cancellationToken: ct)) > 0;
        var identityHash = facts.ProtectedIdentity is null ? null : otpHasher.Hash(
            Guid.Empty,
            "demo-bank-identity:" + secretProtector.Unprotect(facts.ProtectedIdentity));
        if (identityHash is not null
            && await c.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT COUNT(*)
            FROM "integration"."localBankingIdentities" AS "demo_bank_identities"
            WHERE "demo_bank_identities"."identityHash" = @Hash
            AND(
                SELECT "related1"."publicId"
                FROM "identity"."users" AS "related1"
                WHERE "related1"."id" = "demo_bank_identities"."userId") <> @Customer
            """,
            new { Hash = identityHash, Customer = customer },
            tx,
            cancellationToken: ct)) > 0)
        {
            throw new PortalConflictException("This identity already has a banking profile. Use the existing customer account.");
        }

        if (existing)
        {
            if (identityHash is not null)
            {
                await c.ExecuteAsync(new CommandDefinition(
                    """
                UPDATE "integration"."localBankingIdentities"
                SET "identityHash" = @Hash
                WHERE(
                    SELECT "related1"."publicId"
                    FROM "identity"."users" AS "related1"
                    WHERE "related1"."id" = "localBankingIdentities"."userId") = @Customer
                AND "localBankingIdentities"."identityHash" IS NULL
                """,
                    new { Hash = identityHash, Customer = customer },
                    tx,
                    cancellationToken: ct));
            }
        }
        else
        {
            await c.ExecuteAsync(new CommandDefinition(
                """
            INSERT INTO "integration"."localBankingIdentities"("userId",
                "identityHash",
                "createdAt")
            VALUES((
                    SELECT "resolved1"."id"
                    FROM "identity"."users" AS "resolved1"
                    WHERE "resolved1"."publicId" = @Customer),
                @Hash,
                @Now)
            """,
                new
                {
                    Customer = customer,
                    Hash = identityHash,
                    Now = DateTimeOffset.UtcNow
                },
                tx,
                cancellationToken: ct));
        }
    }

    public async Task<bool> EnsureFirstLoginAccounts(Guid customer, CancellationToken ct)
    {
        await using var c = await connections.OpenConnection(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await LockOperations(c, tx, ct);
        if (await c.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT COUNT(*)
            FROM "integration"."localBankingOnboarding" AS "demo_bank_onboarding"
            WHERE(
                SELECT "related1"."publicId"
                FROM "identity"."users" AS "related1"
                WHERE "related1"."id" = "demo_bank_onboarding"."userId") = @Customer
            """,
            new { Customer = customer },
            tx,
            cancellationToken: ct)) > 0)
        {
            await tx.CommitAsync(ct);
            return false;
        }

        var facts = await ProvisionedIdentity(c, tx, customer, ct);
        if (!facts.ContactVerified)
        {
            await tx.CommitAsync(ct);
            return false;
        }

        await LinkProvisionedIdentity(c, tx, customer, ct);
        var now = ProvisioningNow();
        var existingTypes = (await c.QueryAsync<string>(new CommandDefinition(
            """
            SELECT "customer_accounts"."accountType" AS "account_type"
            FROM "accounts"."accounts" AS "customer_accounts"
            WHERE(
                SELECT "related1"."publicId"
                FROM "identity"."users" AS "related1"
                WHERE "related1"."id" = "customer_accounts"."userId") = @Customer
            AND "customer_accounts"."accountStatus" <> 'Closed'
            AND "customer_accounts"."accountType" IN('Cheque',
                'Savings')
            """,
            new { Customer = customer },
            tx,
            cancellationToken: ct))).ToHashSet(StringComparer.Ordinal);
        foreach (var accountType in CustomerAccountPolicy.AccountTypes.Where(type => !existingTypes.Contains(type)))
        {
            var id = Guid.NewGuid();
            var batch = Guid.NewGuid();
            var number = "••••" + System.Security.Cryptography.RandomNumberGenerator.GetInt32(1000, 10000);
            await c.ExecuteAsync(new CommandDefinition(
                """
                    INSERT INTO "accounts"."accounts"("publicId",
                        "userId",
                        "accountType",
                        "currencyCode",
                        "maskedAccountNumber")
                    VALUES(@Id,
                        (
                            SELECT "resolved1"."id"
                            FROM "identity"."users" AS "resolved1"
                            WHERE "resolved1"."publicId" = @Customer),
                        @Type,
                        'ZAR',
                        @Number);
                    INSERT INTO "integration"."localBankingAccounts"("accountId",
                        "bankingIdentityId",
                        "openingRequestId",
                        "initialCount")
                    VALUES((
                            SELECT "resolved2"."id"
                            FROM "accounts"."accounts" AS "resolved2"
                            WHERE "resolved2"."publicId" = @Id),
                        (
                            SELECT "resolved3"."id"
                            FROM "integration"."localBankingIdentities" AS "resolved3"
                            WHERE(
                                SELECT "related4"."publicId"
                                FROM "identity"."users" AS "related4"
                                WHERE "related4"."id" = "resolved3"."userId") = @Customer),
                        @Batch,
                        30)
                    """,
                new
                {
                    Id = id,
                    Customer = customer,
                    Type = accountType,
                    Number = number,
                    Batch = batch
                },
                tx,
                cancellationToken: ct));
            await InsertTransactionBatch(c, tx, customer, id, batch, 30, now, ct);
            await GenerateSyntheticBatch(
                c,
                tx,
                new BankingBatchDto
                {
                    Id = batch,
                    AccountId = id,
                    Count = 30,
                    State = "Pending",
                    Attempts = 0,
                    CreatedAt = now,
                    CompletedAt = null
                },
                now,
                "BANK-",
                ct);
            await AuditOperation(
                c,
                tx,
                customer,
                "LocalBankAccountOpened",
                id,
                new
                {
                    AccountType = accountType,
                    SampleCount = 30,
                    Source = "First verified login"
                },
                ct);
        }

        await c.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO "integration"."localBankingOnboarding"("userId",
                "completedAt")
            VALUES((
                    SELECT "resolved1"."id"
                    FROM "identity"."users" AS "resolved1"
                    WHERE "resolved1"."publicId" = @Customer),
                @Now)
            """,
            new { Customer = customer, Now = now },
            tx,
            cancellationToken: ct));
        await tx.CommitAsync(ct);
        if (realtime is not null)
        {
            await realtime.PublishPortalUpdate([customer], false, ["banking"], null, ct);
        }

        return true;
    }

    public async Task<ProvisionedAccountCreatedDto> OpenAccount(
        Guid customer,
        Guid requestId,
        string accountType,
        int count,
        CancellationToken ct)
    {
        ValidateProvisionedRequest(requestId, count);
        if (!CustomerAccountPolicy.CanOpen(accountType))
        {
            throw new PortalValidationException("Choose Cheque or Savings.");
        }

        await using var c = await connections.OpenConnection(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await LockOperations(c, tx, ct);
        await LinkProvisionedIdentity(c, tx, customer, ct);
        var old = await c.QuerySingleOrDefaultAsync<ProvisionedOpening>(new CommandDefinition(
            """
            SELECT(
                SELECT "related1"."publicId"
                FROM "accounts"."accounts" AS "related1"
                WHERE "related1"."id" = "d"."accountId") AS AccountId,
            (
                SELECT(
                    SELECT "related3"."publicId"
                    FROM "identity"."users" AS "related3"
                    WHERE "related3"."id" = "related2"."userId")
                FROM "integration"."localBankingIdentities" AS "related2"
                WHERE "related2"."id" = "d"."bankingIdentityId") AS UserId,
            "a"."accountType" AS AccountType,
            "d"."initialCount" AS InitialCount
            FROM "integration"."localBankingAccounts" d
            JOIN "accounts"."accounts" a
            ON "a"."id" = "d"."accountId"
            WHERE "d"."openingRequestId" = @Id
            """,
            new { Id = requestId },
            tx,
            cancellationToken: ct));
        if (old is not null)
        {
            if (old.UserId != customer || old.AccountType != accountType || old.InitialCount != count)
            {
                throw new PortalConflictException("This request reference was already used. Start a new request.");
            }

            await tx.CommitAsync(ct);
            return new global::DisputePortal.Api.Models.Responses.ProvisionedAccountCreatedDto
            {
                AccountId = old.AccountId,
                BatchId = requestId
            };
        }

        if (await c.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT COUNT(*)
            FROM "integration"."localTransactionBatches" AS "demo_transaction_batches"
            WHERE "demo_transaction_batches"."publicId" = @Id
            """,
            new { Id = requestId },
            tx,
            cancellationToken: ct)) > 0)
        {
            throw new PortalConflictException("This request reference was already used.");
        }

        if (await c.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT COUNT(*)
            FROM "accounts"."accounts" AS "customer_accounts"
            WHERE(
                SELECT "related1"."publicId"
                FROM "identity"."users" AS "related1"
                WHERE "related1"."id" = "customer_accounts"."userId") = @Customer
            AND "customer_accounts"."accountType" IN('Cheque',
                'Savings')
            AND "customer_accounts"."accountStatus" <> 'Closed'
            """,
            new { Customer = customer },
            tx,
            cancellationToken: ct)) >= CustomerAccountPolicy.MaximumAccounts)
        {
            throw new PortalConflictException("Your Cheque and Savings accounts are already available.");
        }

        if (await c.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT COUNT(*)
            FROM "accounts"."accounts" AS "customer_accounts"
            WHERE(
                SELECT "related1"."publicId"
                FROM "identity"."users" AS "related1"
                WHERE "related1"."id" = "customer_accounts"."userId") = @Customer
            AND "customer_accounts"."accountType" = @Type
            AND "customer_accounts"."accountStatus" <> 'Closed'
            """,
            new { Customer = customer, Type = accountType },
            tx,
            cancellationToken: ct)) > 0)
        {
            throw new PortalConflictException("You already have this account type.");
        }

        var id = Guid.NewGuid();
        var now = ProvisioningNow();
        var number = "••••" + System.Security.Cryptography.RandomNumberGenerator.GetInt32(1000, 10000);
        await c.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO "accounts"."accounts"("publicId",
                "userId",
                "accountType",
                "currencyCode",
                "maskedAccountNumber")
            VALUES(@Id,
                (
                    SELECT "resolved1"."id"
                    FROM "identity"."users" AS "resolved1"
                    WHERE "resolved1"."publicId" = @Customer),
                @Type,
                'ZAR',
                @Number);
            INSERT INTO "integration"."localBankingAccounts"("accountId",
                "bankingIdentityId",
                "openingRequestId",
                "initialCount")
            VALUES((
                    SELECT "resolved2"."id"
                    FROM "accounts"."accounts" AS "resolved2"
                    WHERE "resolved2"."publicId" = @Id),
                (
                    SELECT "resolved3"."id"
                    FROM "integration"."localBankingIdentities" AS "resolved3"
                    WHERE(
                        SELECT "related4"."publicId"
                        FROM "identity"."users" AS "related4"
                        WHERE "related4"."id" = "resolved3"."userId") = @Customer),
                @Request,
                @Count)
            """,
            new
            {
                Id = id,
                Customer = customer,
                Type = accountType,
                Number = number,
                Request = requestId,
                Count = count
            },
            tx,
            cancellationToken: ct));
        await InsertTransactionBatch(c, tx, customer, id, requestId, count, now, ct);
        await AuditOperation(c, tx, customer, "LocalBankAccountOpened", id, new { AccountType = accountType, SampleCount = count }, ct);
        await tx.CommitAsync(ct);
        return new global::DisputePortal.Api.Models.Responses.ProvisionedAccountCreatedDto
        {
            AccountId = id,
            BatchId = requestId
        };
    }

    private Task InsertTransactionBatch(
        DbConnection c,
        DbTransaction tx,
        Guid customer,
        Guid account,
        Guid request,
        int count,
        DateTimeOffset now,
        CancellationToken ct) => c.ExecuteAsync(new CommandDefinition(
        """
        INSERT INTO "integration"."localTransactionBatches"("publicId",
            "bankingAccountId",
            "userId",
            "transactionCount",
            "state",
            "createdAt")
        VALUES(@Request,
            (
                SELECT "resolved1"."id"
                FROM "integration"."localBankingAccounts" AS "resolved1"
                WHERE(
                    SELECT "related2"."publicId"
                    FROM "accounts"."accounts" AS "related2"
                    WHERE "related2"."id" = "resolved1"."accountId") = @Account),
            (
                SELECT "resolved3"."id"
                FROM "identity"."users" AS "resolved3"
                WHERE "resolved3"."publicId" = @Customer),
            @Count,
            'Pending',
            @Now)
        """,
        new
        {
            Request = request,
            Account = account,
            Customer = customer,
            Count = count,
            Now = now
        },
        tx,
        cancellationToken: ct));

    public async Task<BankingBatchDto> QueueTransactions(Guid customer, Guid account, Guid requestId, int count, CancellationToken ct)
    {
        ValidateProvisionedRequest(requestId, count);
        await using var c = await connections.OpenConnection(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await LockOperations(c, tx, ct);
        if (await c.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT COUNT(*)
            FROM "integration"."localBankingAccounts" d
            JOIN "accounts"."accounts" a
            ON "a"."id" = "d"."accountId"
            WHERE(
                SELECT "related1"."publicId"
                FROM "accounts"."accounts" AS "related1"
                WHERE "related1"."id" = "d"."accountId") = @Account
            AND(
                SELECT(
                    SELECT "related3"."publicId"
                    FROM "identity"."users" AS "related3"
                    WHERE "related3"."id" = "related2"."userId")
                FROM "integration"."localBankingIdentities" AS "related2"
                WHERE "related2"."id" = "d"."bankingIdentityId") = @Customer
            AND(
                SELECT "related4"."publicId"
                FROM "identity"."users" AS "related4"
                WHERE "related4"."id" = "a"."userId") = @Customer
            AND "a"."accountStatus" = 'Active'
            """,
            new { Account = account, Customer = customer },
            tx,
            cancellationToken: ct)) != 1)
        {
            throw new PortalNotFoundException();
        }

        await LinkProvisionedIdentity(c, tx, customer, ct);
        var old = await c.QuerySingleOrDefaultAsync<BankingBatchDto>(new CommandDefinition(
            DemoBatchSelect + """
            
            WHERE "b"."publicId" = @Id
            """,
            new { Id = requestId },
            tx,
            cancellationToken: ct));
        if (old is not null)
        {
            if (old.AccountId != account || old.Count != count)
            {
                throw new PortalConflictException("This request reference was already used.");
            }

            await tx.CommitAsync(ct);
            return old;
        }

        if (await c.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT COUNT(*)
            FROM "integration"."localTransactionBatches" AS "demo_transaction_batches"
            WHERE(
                SELECT(
                    SELECT "related2"."publicId"
                    FROM "accounts"."accounts" AS "related2"
                    WHERE "related2"."id" = "related1"."accountId")
                FROM "integration"."localBankingAccounts" AS "related1"
                WHERE "related1"."id" = "demo_transaction_batches"."bankingAccountId") = @Account
            AND "demo_transaction_batches"."state" = 'Pending'
            """,
            new { Account = account },
            tx,
            cancellationToken: ct)) > 0)
        {
            throw new PortalConflictException("A transaction batch is already queued for this account.");
        }

        if (await c.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT COUNT(*)
            FROM "transactions"."transactions" AS "transactions"
            WHERE(
                SELECT "related1"."publicId"
                FROM "accounts"."accounts" AS "related1"
                WHERE "related1"."id" = "transactions"."accountId") = @Account
            """,
            new { Account = account },
            tx,
            cancellationToken: ct)) + count > 500)
        {
            throw new PortalConflictException("This local account has reached its 500-transaction limit.");
        }

        var now = ProvisioningNow();
        await InsertTransactionBatch(c, tx, customer, account, requestId, count, now, ct);
        await AuditOperation(c, tx, customer, "LocalTransactionsQueued", account, new { Count = count }, ct);
        await tx.CommitAsync(ct);
        return new global::DisputePortal.Api.Models.Responses.BankingBatchDto
        {
            Id = requestId,
            AccountId = account,
            Count = count,
            State = "Pending",
            Attempts = 0,
            CreatedAt = now,
            CompletedAt = null
        };
    }

    public async Task<bool> ProcessNextTransactionBatch(CancellationToken ct)
    {
        await using var c = await connections.OpenConnection(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await LockOperations(c, tx, ct);
        var batch = await c.QuerySingleOrDefaultAsync<BankingBatchDto>(new CommandDefinition(
            DemoBatchSelect + """
            
            WHERE "b"."state" = 'Pending'
            ORDER BY "b"."createdAt",
            "b"."publicId"
            OFFSET 0 ROWS
            FETCH NEXT 1 ROWS ONLY
            """,
            transaction: tx,
            cancellationToken: ct));
        if (batch is null)
        {
            await tx.CommitAsync(ct);
            return false;
        }

        var customer = await c.ExecuteScalarAsync<Guid>(new CommandDefinition(
            """
            SELECT(
                SELECT "related1"."publicId"
                FROM "identity"."users" AS "related1"
                WHERE "related1"."id" = "demo_transaction_batches"."userId") AS "user_id"
            FROM "integration"."localTransactionBatches" AS "demo_transaction_batches"
            WHERE "demo_transaction_batches"."publicId" = @Id
            """,
            new { batch.Id },
            tx,
            cancellationToken: ct));
        var active = await c.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT COUNT(*)
            FROM "accounts"."accounts" a
            JOIN "identity"."users" u
            ON "u"."id" = "a"."userId"
            WHERE "a"."publicId" = @Id
            AND(
                SELECT "related1"."publicId"
                FROM "identity"."users" AS "related1"
                WHERE "related1"."id" = "a"."userId") = @Customer
            AND "a"."accountStatus" = 'Active'
            AND "u"."accountStatus" = 'Active'
            """,
            new { Id = batch.AccountId, Customer = customer },
            tx,
            cancellationToken: ct));
        var now = ProvisioningNow();
        if (active == 1)
        {
            await GenerateSyntheticBatch(c, tx, batch, now, "BANK-", ct);
        }
        else
        {
            await c.ExecuteAsync(new CommandDefinition(
                """
            UPDATE "integration"."localTransactionBatches"
            SET "state" = 'Failed',
            "attempts" = "localTransactionBatches"."attempts" + 1,
            "completedAt" = @Now
            WHERE "localTransactionBatches"."publicId" = @Id
            """,
                new { batch.Id, Now = now },
                tx,
                cancellationToken: ct));
        }

        // Transactions and the handled outbox record commit together. A crash rolls both back;
        // multiple workers serialize on the operations lock and cannot duplicate a completed batch.
        await tx.CommitAsync(ct);
        if (realtime is not null)
        {
            await realtime.PublishPortalUpdate([customer], false, ["banking"], batch.AccountId, ct);
        }

        return true;
    }

    private sealed class ProvisionedIdentityFacts
    {
        public string? ProtectedIdentity { get; set; }
        public bool ContactVerified { get; set; }
    }

    private sealed class ProvisionedOpening
    {
        public Guid AccountId { get; set; }
        public Guid UserId { get; set; }
        public string AccountType { get; set; } = "";
        public int InitialCount { get; set; }
    }
}
