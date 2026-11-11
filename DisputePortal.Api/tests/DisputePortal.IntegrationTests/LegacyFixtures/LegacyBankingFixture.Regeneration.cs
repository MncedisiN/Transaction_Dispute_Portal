using DisputePortal.Api.Repository.Interfaces;
using Dapper;
using System.Data.Common;
using System.Text;
using System.Security.Cryptography;
using System.Text.Json;
using DisputePortal.Api.Models.Domain;
using DisputePortal.Api.Models.Exceptions;
using DisputePortal.Api.Models.Responses;

namespace DisputePortal.Api.Repository;

public sealed partial class LegacyBankingFixture : ILocalBankingAdministrationRepository
{
    // Reviewed seed identity pairs only. This must never become a general unlinked-account adoption rule.
    private const string LegacyFixtureAccountSql = """
        ((a."publicId" = '20000000-0000-0000-0000-000000000001' AND u."publicId" = '10000000-0000-0000-0000-000000000001')
            OR (a."publicId" = '20000000-0000-0000-0000-000000000002' AND u."publicId" = '10000000-0000-0000-0000-000000000001')
            OR (a."publicId" = '20000000-0000-0000-0000-000000000003' AND u."publicId" = '10000000-0000-0000-0000-000000000003'))
        """;
    private const string RegenerationEligibleSql = """
        (EXISTS (
            SELECT 1
            FROM "integration"."localBankingAccounts" AS b
            JOIN "integration"."localBankingIdentities" AS identityRecord
                ON identityRecord."id" = b."bankingIdentityId"
            WHERE b."accountId" = a."id"
                AND identityRecord."userId" = a."userId") OR
        """ + LegacyFixtureAccountSql + ")";

    private async Task RequireBankingAdministrator(DbConnection c, DbTransaction? tx, Guid actor, CancellationToken ct)
    {
        if (!(await Roles(c, tx, actor, ct)).Contains("super_admin", StringComparer.Ordinal))
        {
            throw new PortalForbiddenException("Only an administrator can regenerate local banking transactions.");
        }
    }

    public async Task<IReadOnlyList<BankingRegenerationTarget>> GetRegenerationTargets(Guid actor, CancellationToken ct)
    {
        await using var c = await connections.OpenConnection(ct);
        await RequireBankingAdministrator(c, null, actor, ct);
        return (await c.QueryAsync<BankingRegenerationTarget>(new CommandDefinition(
            """
            SELECT "a"."publicId" AS AccountId,
            "p"."displayName" AS CustomerName,
            "a"."accountType" AS AccountType,
            "a"."maskedAccountNumber" AS AccountNumber,
            (
                SELECT CAST(COUNT(*) AS int)
                FROM "transactions"."transactions" t
                WHERE "t"."accountId" = "a"."id"
                AND "t"."archivedAt" IS NULL) AS TransactionCount
            FROM "accounts"."accounts" a
            JOIN "identity"."users" u
            ON "u"."id" = "a"."userId"
            JOIN "identity"."customerProfiles" p
            ON "p"."userId" = "u"."id"
            WHERE "a"."accountStatus" = 'Active'
            AND "u"."accountStatus" = 'Active'
            AND "a"."currencyCode" = 'ZAR'
            AND
            
            """ + RegenerationEligibleSql + """
            
            AND NOT EXISTS(
                SELECT 1
                FROM "accounts"."bankLinks" l
                WHERE "l"."accountId" = "a"."id")
            ORDER BY "p"."displayName",
            "a"."accountType",
            "a"."publicId"
            """,
            cancellationToken: ct))).AsList();
    }

    public async Task<BankingRegenerationResult> RegenerateTransactions(Guid actor, RegenerateBankingRequest request, CancellationToken ct)
    {
        if (request.RequestId == Guid.Empty
            || request.AccountId == Guid.Empty
            || request.CountPerAccount is < 30 or > 50
            || string.IsNullOrWhiteSpace(request.Reason)
            || request.Reason.Trim().Length is < 10 or > 500
            || request.Reason.Any(char.IsControl))
        {
            throw new PortalValidationException("Supply a request reference, between 30 and 50 transactions, and a reason of 10 to 500 characters.");
        }

        var fingerprint = SourceHash(JsonSerializer.Serialize(new
        {
            Actor = actor,
            request.AccountId,
            request.CountPerAccount,
            Reason = request.Reason.Trim()
        }));
        await using var c = await connections.OpenConnection(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await LockOperations(c, tx, ct);
        await RequireBankingAdministrator(c, tx, actor, ct);
        var old = await c.QuerySingleOrDefaultAsync<PriorGeneration>(new CommandDefinition(
            """
            SELECT "local_banking_regenerations"."fingerprint" AS "fingerprint",
            "local_banking_regenerations"."accountCount" AS AccountCount,
            "local_banking_regenerations"."archivedCount" AS ArchivedCount,
            "local_banking_regenerations"."createdCount" AS CreatedCount
            FROM "integration"."bankingRegenerations" AS "local_banking_regenerations"
            WHERE "local_banking_regenerations"."publicId" = @Id
            """,
            new { Id = request.RequestId },
            tx,
            cancellationToken: ct));
        if (old is not null)
        {
            if (old.Fingerprint != fingerprint)
            {
                throw new PortalConflictException("This regeneration request reference was already used with different details.");
            }

            await tx.CommitAsync(ct);
            return new global::DisputePortal.Api.Repository.Interfaces.BankingRegenerationResult
            {
                RequestId = request.RequestId,
                AccountCount = old.AccountCount,
                ArchivedCount = old.ArchivedCount,
                CreatedCount = old.CreatedCount
            };
        }

        var accounts = (await c.QueryAsync<RegenerationAccount>(new CommandDefinition(
            $$"""
            SELECT a."publicId" AS AccountId,
                u."publicId" AS CustomerId,
                CASE WHEN {{LegacyFixtureAccountSql}} THEN 1 ELSE 0 END AS LegacyFixture
            FROM "accounts"."accounts" AS a
            JOIN "identity"."users" AS u
                ON u."id" = a."userId"
            WHERE a."accountStatus" = 'Active'
                AND u."accountStatus" = 'Active'
                AND a."currencyCode" = 'ZAR'
                AND {{RegenerationEligibleSql}}

            
            AND(@Account IS NULL
                OR "a"."publicId" = @Account)
            AND NOT EXISTS(
                SELECT 1
                FROM "accounts"."bankLinks" l
                WHERE "l"."accountId" = "a"."id")
            ORDER BY "a"."publicId"
            """,
            new { Account = request.AccountId },
            tx,
            cancellationToken: ct))).ToArray();
        if (accounts.Length == 0)
        {
            throw new PortalNotFoundException("No active locally provisioned account matches this request.");
        }

        if (accounts.Length > 200)
        {
            throw new PortalValidationException("Regenerate a selected account when more than 200 local accounts are available.");
        }

        var now = ProvisioningNow();
        var archived = 0;
        var created = 0;
        foreach (var account in accounts)
        {
            ct.ThrowIfCancellationRequested();
            if (account.LegacyFixture == 1)
            {
                await AdoptReviewedFixture(c, tx, actor, account, request.RequestId, request.CountPerAccount, now, ct);
            }

            // Only known generated rows are archived. Cases, refunds, source snapshots and journal history stay intact.
            var postings = (await c.QueryAsync<ArchivePosting>(new CommandDefinition(
                """
                SELECT "t"."publicId" AS "id",
                COALESCE(SUM("l"."currentDelta"),
                    0) AS CurrentDelta,
                COALESCE(SUM("l"."reservedDelta"),
                    0) AS ReservedDelta,
                CAST(COALESCE(SUM(CAST("l"."unknownDelta" AS int)),
                        0) AS int) AS UnknownDelta
                FROM "transactions"."transactions" t
                LEFT JOIN "ledger"."projectionEntries" l
                ON "l"."transactionId" = "t"."id"
                WHERE(
                    SELECT "related1"."publicId"
                    FROM "accounts"."accounts" AS "related1"
                    WHERE "related1"."id" = "t"."accountId") = @Account
                AND "t"."archivedAt" IS NULL
                AND("t"."transactionReference" LIKE 'BANK-%'
                    OR "t"."transactionReference" LIKE 'DEMO-%'
                    OR "t"."transactionReference" LIKE 'OPENING-%'
                    OR "t"."transactionReference" LIKE 'REGEN-%'
                    OR(@LegacyFixture = 1
                        AND("t"."transactionReference" LIKE 'TX-DEMO-%'
                            OR "t"."transactionReference" LIKE 'TX-OTHER-%')))
                AND NOT EXISTS(
                    SELECT 1
                    FROM "transactions"."bankLinks" b
                    WHERE "b"."transactionId" = "t"."id")
                GROUP BY "t"."id",
                "t"."publicId"
                """,
                new { Account = account.AccountId, account.LegacyFixture },
                tx,
                cancellationToken: ct))).ToArray();
            foreach (var posting in postings)
            {
                // Append the exact inverse, preserving every previous journal entry and transaction status.
                if (posting.CurrentDelta != 0 || posting.ReservedDelta != 0 || posting.UnknownDelta != 0)
                {
                    await c.ExecuteAsync(new CommandDefinition(
                        """
                        INSERT INTO "ledger"."projectionEntries"("accountId",
                            "transactionId",
                            "currentDelta",
                            "reservedDelta",
                            "unknownDelta",
                            "eventKind")
                        VALUES((
                                SELECT "resolved1"."id"
                                FROM "accounts"."accounts" AS "resolved1"
                                WHERE "resolved1"."publicId" = @Account),
                            (
                                SELECT "resolved2"."id"
                                FROM "transactions"."transactions" AS "resolved2"
                                WHERE "resolved2"."publicId" = @Transaction),
                            @Current,
                            @Reserved,
                            @Unknown,
                            'Correction')
                        """,
                        new
                        {
                            Account = account.AccountId,
                            Transaction = posting.Id,
                            Current = -posting.CurrentDelta,
                            Reserved = -posting.ReservedDelta,
                            Unknown = -posting.UnknownDelta
                        },
                        tx,
                        cancellationToken: ct));
                }

                archived += await c.ExecuteAsync(new CommandDefinition(
                    """
                    UPDATE "transactions"."transactions"
                    SET "archivedAt" = @Now
                    WHERE "transactions"."publicId" = @Id
                    AND "transactions"."archivedAt" IS NULL
                    """,
                    new { posting.Id, Now = now },
                    tx,
                    cancellationToken: ct));
            }

            await c.ExecuteAsync(new CommandDefinition(
                """
                UPDATE "integration"."localTransactionBatches"
                SET "state" = 'Failed',
                "completedAt" = @Now
                WHERE(
                    SELECT(
                        SELECT "related2"."publicId"
                        FROM "accounts"."accounts" AS "related2"
                        WHERE "related2"."id" = "related1"."accountId")
                    FROM "integration"."localBankingAccounts" AS "related1"
                    WHERE "related1"."id" = "localTransactionBatches"."bankingAccountId") = @Account
                AND "localTransactionBatches"."state" = 'Pending'
                """,
                new { Account = account.AccountId, Now = now },
                tx,
                cancellationToken: ct));
            var batch = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(request.RequestId.ToString("N") + ":" + account.AccountId.ToString("N")))[..16]);
            await InsertTransactionBatch(c, tx, account.CustomerId, account.AccountId, batch, request.CountPerAccount, now, ct);
            var dto = new BankingBatchDto
            {
                Id = batch,
                AccountId = account.AccountId,
                Count = request.CountPerAccount,
                State = "Pending",
                Attempts = 0,
                CreatedAt = now,
                CompletedAt = null
            };
            created += await GenerateSyntheticBatch(c, tx, dto, now, "BANK-", ct);
        }

        await c.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO "integration"."bankingRegenerations"("publicId",
                "actorId",
                "fingerprint",
                "protectedReason",
                "accountCount",
                "archivedCount",
                "createdCount",
                "completedAt")
            SELECT @Id,
            "u"."id",
            @Fingerprint,
            @Reason,
            @Accounts,
            @Archived,
            @Created,
            @Now
            FROM "identity"."users" u
            WHERE "u"."publicId" = @Actor
            """,
            new
            {
                Id = request.RequestId,
                Actor = actor,
                Fingerprint = fingerprint,
                Reason = secretProtector.Protect(request.Reason.Trim()),
                Accounts = accounts.Length,
                Archived = archived,
                Created = created,
                Now = now
            },
            tx,
            cancellationToken: ct));
        await AuditOperation(
            c,
            tx,
            actor,
            "LocalBankingTransactionsRegenerated",
            request.RequestId,
            new
            {
                AccountCount = accounts.Length,
                Archived = archived,
                Created = created,
                request.AccountId,
                request.CountPerAccount,
                ReasonHash = SourceHash(request.Reason.Trim()),
                Source = "LocalSyntheticBanking"
            },
            ct);
        await tx.CommitAsync(ct);
        if (realtime is not null)
        {
            await realtime.PublishPortalUpdate(
                accounts.Select(a => a.CustomerId).Distinct().ToArray(),
                false,
                ["banking"],
                request.AccountId,
                ct);
        }

        return new global::DisputePortal.Api.Repository.Interfaces.BankingRegenerationResult
        {
            RequestId = request.RequestId,
            AccountCount = accounts.Length,
            ArchivedCount = archived,
            CreatedCount = created
        };
    }

    private async Task AdoptReviewedFixture(
        DbConnection c,
        DbTransaction tx,
        Guid actor,
        RegenerationAccount account,
        Guid request,
        int count,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var existing = await c.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition(
            """
            SELECT(
                SELECT(
                    SELECT "related2"."publicId"
                    FROM "identity"."users" AS "related2"
                    WHERE "related2"."id" = "related1"."userId")
                FROM "integration"."localBankingIdentities" AS "related1"
                WHERE "related1"."id" = "demo_bank_accounts"."bankingIdentityId") AS "user_id"
            FROM "integration"."localBankingAccounts" AS "demo_bank_accounts"
            WHERE(
                SELECT "related3"."publicId"
                FROM "accounts"."accounts" AS "related3"
                WHERE "related3"."id" = "demo_bank_accounts"."accountId") = @Account
            """,
            new { Account = account.AccountId },
            tx,
            cancellationToken: ct));
        if (existing is not null)
        {
            if (existing != account.CustomerId)
            {
                throw new PortalConflictException("This local account's ownership mapping requires review.");
            }

            return;
        }

        await c.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO "integration"."localBankingIdentities"("userId",
                "identityHash",
                "createdAt")
            SELECT(
                SELECT "resolved2"."id"
                FROM "identity"."users" AS "resolved2"
                WHERE "resolved2"."publicId" = @Customer),
            NULL,
            @Now
            WHERE NOT EXISTS(
                SELECT 1
                FROM "integration"."localBankingIdentities" AS "demo_bank_identities"
                WHERE(
                    SELECT "related1"."publicId"
                    FROM "identity"."users" AS "related1"
                    WHERE "related1"."id" = "demo_bank_identities"."userId") = @Customer)
            """,
            new { Customer = account.CustomerId, Now = now },
            tx,
            cancellationToken: ct));
        var openingRequest = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes("reviewed-development-fixture:" + account.AccountId.ToString("N")))[..16]);
        await c.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO "integration"."localBankingAccounts"("accountId",
                "bankingIdentityId",
                "openingRequestId",
                "initialCount")
            VALUES((
                    SELECT "resolved1"."id"
                    FROM "accounts"."accounts" AS "resolved1"
                    WHERE "resolved1"."publicId" = @Account),
                (
                    SELECT "resolved2"."id"
                    FROM "integration"."localBankingIdentities" AS "resolved2"
                    WHERE(
                        SELECT "related3"."publicId"
                        FROM "identity"."users" AS "related3"
                        WHERE "related3"."id" = "resolved2"."userId") = @Customer),
                @Opening,
                @Count)
            """,
            new
            {
                Account = account.AccountId,
                Customer = account.CustomerId,
                Opening = openingRequest,
                Count = count
            },
            tx,
            cancellationToken: ct));
        await AuditOperation(
            c,
            tx,
            actor,
            "LocalBankingFixtureAdopted",
            account.AccountId,
            new
            {
                RequestId = request,
                Source = "ReviewedDevelopmentFixture",
                account.CustomerId
            },
            ct);
    }

    private async Task<int> GenerateSyntheticBatch(
        DbConnection c,
        DbTransaction tx,
        BankingBatchDto batch,
        DateTimeOffset now,
        string prefix,
        CancellationToken ct)
    {
        var policy = await DisputePortal.Api.Repository.PolicyVersionRepository.EffectiveAutomatic(c, tx, DateTimeOffset.UtcNow, ct) ?? throw new InvalidOperationException("No refund policy is effective.");
        var items = BankingTransactionFactory.Create(batch.Id, batch.Count, now, policy.MaximumAmount, policy.MaximumAgeHours);
        var hasOpening = await c.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT COUNT(*)
            FROM "transactions"."transactions" AS "transactions"
            WHERE(
                SELECT "related1"."publicId"
                FROM "accounts"."accounts" AS "related1"
                WHERE "related1"."id" = "transactions"."accountId") = @Account
            AND "transactions"."archivedAt" IS NULL
            AND("transactions"."transactionReference" LIKE 'BANK-OPENING-%'
                OR "transactions"."transactionReference" LIKE 'OPENING-%'
                OR "transactions"."transactionReference" LIKE 'REGEN-OPENING-%')
            """,
            new { Account = batch.AccountId },
            tx,
            cancellationToken: ct)) > 0;
        var created = 0;
        if (!hasOpening)
        {
            var current = await c.ExecuteScalarAsync<decimal>(new CommandDefinition(
                """
                SELECT COALESCE(SUM("financial_ledger"."currentDelta"),
                    0)
                FROM "ledger"."projectionEntries" AS "financial_ledger"
                WHERE(
                    SELECT "related1"."publicId"
                    FROM "accounts"."accounts" AS "related1"
                    WHERE "related1"."id" = "financial_ledger"."accountId") = @Account
                """,
                new { Account = batch.AccountId },
                tx,
                cancellationToken: ct));
            var reserved = await c.ExecuteScalarAsync<decimal>(new CommandDefinition(
                """
                SELECT COALESCE(SUM("financial_ledger"."reservedDelta"),
                    0)
                FROM "ledger"."projectionEntries" AS "financial_ledger"
                WHERE(
                    SELECT "related1"."publicId"
                    FROM "accounts"."accounts" AS "related1"
                    WHERE "related1"."id" = "financial_ledger"."accountId") = @Account
                """,
                new { Account = batch.AccountId },
                tx,
                cancellationToken: ct));
            var amount = BankingTransactionFactory.OpeningCredit(items, current, reserved);
            var openingId = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(batch.Id.ToString("N") + ":opening"))[..16]);
            await c.ExecuteAsync(new CommandDefinition(
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
                    @At,
                    'Opening credit',
                    'Local banking',
                    @Amount,
                    'ZAR',
                    (
                        SELECT "resolved2"."id"
                        FROM "configuration"."transactionTypes" AS "resolved2"
                        WHERE "resolved2"."code" = 'Deposit'),
                    'Credit',
                    'Posted',
                """ + dialect.FalseLiteral + ")",
                new
                {
                    Id = openingId,
                    Account = batch.AccountId,
                    Reference = "BANK-OPENING-" + batch.Id.ToString("N"),
                    At = items.Min(x => x.At).AddDays(-1),
                    Amount = amount
                },
                tx,
                cancellationToken: ct));
            created++;
        }

        foreach (var item in items)
            await c.ExecuteAsync(new CommandDefinition(
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
                    "disputeEligible",
                    "debitOrderScheme",
                    "mandateStatus")
                VALUES(@Id,
                    (
                        SELECT "resolved1"."id"
                        FROM "accounts"."accounts" AS "resolved1"
                        WHERE "resolved1"."publicId" = @Account),
                    @Reference,
                    @At,
                    @Description,
                    @Merchant,
                    @Amount,
                    'ZAR',
                    (
                        SELECT "resolved2"."id"
                        FROM "configuration"."transactionTypes" AS "resolved2"
                        WHERE "resolved2"."code" = @Type),
                    @Direction,
                    @Status,
                    @Eligible,
                    @Scheme,
                    @Mandate)
                """,
            new
            {
                item.Id,
                Account = batch.AccountId,
                Reference = prefix + item.Id.ToString("N"),
                item.At,
                item.Description,
                item.Merchant,
                item.Amount,
                item.Type,
                item.Direction,
                item.Status,
                item.Eligible,
                item.Scheme,
                item.Mandate
            },
            tx,
            cancellationToken: ct));
        foreach (var (original, duplicate) in BankingTransactionFactory.ConfirmedDuplicatePairs(items.Count))
            await c.ExecuteAsync(new CommandDefinition(
            """
                INSERT INTO "transactions"."duplicateConfirmations"("transactionId",
                    "originalTransactionId",
                    "confirmationSource")
                VALUES((
                        SELECT "resolved1"."id"
                        FROM "transactions"."transactions" AS "resolved1"
                        WHERE "resolved1"."publicId" = @Duplicate),
                    (
                        SELECT "resolved2"."id"
                        FROM "transactions"."transactions" AS "resolved2"
                        WHERE "resolved2"."publicId" = @Original),
                    'LocalBankFeed')
                """,
            new { Duplicate = items[duplicate].Id, Original = items[original].Id },
            tx,
            cancellationToken: ct));
        await c.ExecuteAsync(new CommandDefinition(
            """
            UPDATE "integration"."localTransactionBatches"
            SET "state" = 'Completed',
            "attempts" = "localTransactionBatches"."attempts" + 1,
            "completedAt" = @Now
            WHERE "localTransactionBatches"."publicId" = @Id
            """,
            new { batch.Id, Now = now },
            tx,
            cancellationToken: ct));
        await AuditOperation(
            c,
            tx,
            null,
            "LocalTransactionsGenerated",
            batch.AccountId,
            new
            {
                Count = items.Count,
                BatchId = batch.Id,
                Source = "LocalSyntheticBanking"
            },
            ct);
        return items.Count + created;
    }

    private class RegenerationAccount
    {
        public Guid AccountId { get; set; }
        public Guid CustomerId { get; set; }
        public int LegacyFixture { get; set; }
    }

    private class PriorGeneration
    {
        public string Fingerprint { get; set; } = null!;
        public int AccountCount { get; set; }
        public int ArchivedCount { get; set; }
        public int CreatedCount { get; set; }
    }

    private class ArchivePosting
    {
        public Guid Id { get; set; }
        public decimal CurrentDelta { get; set; }
        public decimal ReservedDelta { get; set; }
        public int UnknownDelta { get; set; }
    }

    private class GenerationPolicy
    {
        public decimal MaximumAmount { get; set; }
        public int MaximumAgeHours { get; set; }
    }
}
