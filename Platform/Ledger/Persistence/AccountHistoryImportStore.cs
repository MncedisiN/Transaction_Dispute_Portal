using System.Data.Common;
using Dapper;
using DisputePortal.Platform.Contracts;
using DisputePortal.Platform.Infrastructure;
using DisputePortal.Platform.Ledger.Domain;
using DisputePortal.Platform.Infrastructure.Interfaces;
using DisputePortal.Platform.Infrastructure.Models;
using DisputePortal.Platform.Ledger.Interfaces;
using DisputePortal.Platform.Infrastructure.Sql;

namespace DisputePortal.Platform.Ledger;

public class AccountHistoryImportStore(ServiceDatabase db, IServiceClient client) : IAccountHistoryImportStore
{
    public static readonly string Schema = SqlScripts.ReadSqlScript("PostgreSql/Schema/Ledger/AccountHistoryImportStore/CreateAccountHistoryImportSchema");


    public async Task<AccountHistoryImportResult> Import(AccountHistorySnapshot book, CancellationToken ct)
    {
        book.ValidateAccountHistorySnapshot();
        var attemptId = Guid.NewGuid();
        var settlementEventId = Guid.NewGuid();
        var beginStarted = false;
        try
        {
            var settings = await client.SendServiceRequest<PlatformSettings>("configuration", HttpMethod.Get, "/settings", ct: ct);
            beginStarted = true;
            var operation = await client.SendServiceRequest<CountryOperation>(
                "configuration",
                HttpMethod.Post,
                "/country-operations",
                new CountryOperationRequest
                {
                    RequestId = attemptId,
                    SourceKind = "Import",
                    CountryCode = book.CountryCode,
                    Currency = book.Currency,
                    ExpectedVersion = settings.Version
                },
                ct: ct);
            if (operation.RequestId != attemptId
                || operation.OperationId == Guid.Empty
                || operation.SourceKind != "Import"
                || operation.CountryCode != book.CountryCode
                || operation.Currency != book.Currency
                || operation.SettledAt is not null)
            {
                throw new PlatformException(409, "operation_settled", "Review the approved book import again.");
            }

            await using var c = await db.OpenServiceDatabaseConnection(ct);
            await using var tx = await c.BeginTransactionAsync(ct);
            await c.ExecuteAsync(new CommandDefinition(
                """
                SELECT
                    pg_advisory_xact_lock(hashtext(@Id))
                """,
                new
                {
                    Id = "import:" + book.AccountId
                },
                tx,
                cancellationToken: ct));
            var source = await c.QuerySingleOrDefaultAsync<LedgerPosition>(new CommandDefinition(
                SqlScripts.ReadSqlScript("PostgreSql/Queries/Ledger/AccountHistoryImportStore/LockAccountForHistoryImport"),
                new
                {
                    Id = book.AccountId
                },
                tx,
                cancellationToken: ct));
            var prior = source is null ? null : await c.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
                """
                SELECT
                    "sourceHash"
                FROM ledger."bookImports"
                WHERE "accountId" = @Id
                """,
                new
                {
                    Id = source.InternalId
                },
                tx,
                cancellationToken: ct));
            if (prior is not null)
            {
                if (prior != book.Hash)
                {
                    throw new PlatformException(409, "import_conflict", "This account has a different approved opening book.");
                }

                if (source is null)
                {
                    throw new InvalidOperationException("An imported ledger account is missing.");
                }

                await EnqueueSettlement(c, tx, operation, settlementEventId, book.SnapshotId, ct);
                await tx.CommitAsync(ct);
                return new(book.AccountId, book.Transactions.Length, source.Balance, source.Reserved, source.Version, true);
            }

            var currentOperation = await client.SendServiceRequest<CountryOperation>(
                "configuration",
                HttpMethod.Get,
                $"/country-operations/operations/{operation.OperationId}",
                ct: ct);
            if (currentOperation.SettledAt is not null
                || currentOperation.SourceKind != "Import"
                || currentOperation.RequestId != attemptId
                || currentOperation.OperationId != operation.OperationId
                || currentOperation.CountryCode != book.CountryCode
                || currentOperation.Currency != book.Currency)
            {
                throw new PlatformException(409, "operation_settled", "Review the approved book import again.");
            }

            if (source is null)
            {
                await c.ExecuteAsync(new CommandDefinition(
                    SqlScripts.ReadSqlScript("PostgreSql/Queries/Ledger/AccountHistoryImportStore/CreateCustomerAccountForHistoryImportIfMissing"),
                    new
                    {
                        book.AccountId,
                        book.UserId,
                        book.Currency,
                        book.CountryCode,
                        Status = CountryCurrencyPolicy.IsCurrent(settings, book.CountryCode, book.Currency) ? "Active" : "Blocked",
                        PolicyVersion = operation.PolicyVersion
                    },
                    tx,
                    cancellationToken: ct));
                source = await c.QuerySingleAsync<LedgerPosition>(new CommandDefinition(
                    SqlScripts.ReadSqlScript("PostgreSql/Queries/Ledger/AccountHistoryImportStore/LockAccountForHistoryImport"),
                    new
                    {
                        Id = book.AccountId
                    },
                    tx,
                    cancellationToken: ct));
            }

            if (source.UserId != book.UserId
                || source.Currency != book.Currency
                || source.CountryCode != book.CountryCode
                || source.Kind != "Customer"
                || source.Balance != 0
                || source.Reserved != 0
                || await c.ExecuteScalarAsync<int>(new CommandDefinition(
                """
                SELECT
                    COUNT(*)
                FROM ledger."entries"
                WHERE "accountId" = @Id
                """,
                new
                {
                    Id = source.InternalId
                },
                tx,
                cancellationToken: ct)) != 0)
            {
                throw new PlatformException(409, "import_account_in_use", "An opening book requires its matching empty ledger account.");
            }

            await c.ExecuteAsync(new CommandDefinition(
                """
                UPDATE ledger."accounts"
                SET
                    status = CASE WHEN @Eligible THEN status ELSE 'Blocked' END,
                    "countryPolicyVersion" = @PolicyVersion
                WHERE id = @InternalId
                AND status = 'Active'
                AND "countryPolicyVersion" <= @PolicyVersion
                """,
                new
                {
                    source.InternalId,
                    PolicyVersion = operation.PolicyVersion,
                    Eligible = CountryCurrencyPolicy.IsCurrent(settings, source.CountryCode, source.Currency)
                },
                tx,
                cancellationToken: ct));
            var dictionary = new Dictionary<Guid, LedgerPosition>
            {
                {
                    source.AccountId,
                    source
                }
            };
            foreach (var item in book.Transactions.Where(t => t.CurrentDelta != 0).OrderBy(t => t.OccurredAt).ThenBy(t => t.Id))
            {
                var kind = item.Type == "Refund" ? "Refund" : item.Type == "RefundReversal" ? "Recall" : item.Direction == "Debit" ? "ExternalDebit" : "ExternalCredit";
                var r = new PostJournalRequest
                {
                    TransactionId = item.Id,
                    UserId = book.UserId,
                    Kind = kind,
                    Reference = "LEG-" + item.Id.ToString("N"),
                    AccountId = book.AccountId,
                    RecipientAccountId = null,
                    Amount = Math.Abs(item.CurrentDelta),
                    Fee = 0,
                    Currency = book.Currency,
                    TargetAmount = null,
                    TargetCurrency = null,
                    QuoteId = null,
                    OriginalTransactionId = item.OriginalTransactionId
                };
                var builder = new PlanBuilder(r);
                var clearing = builder.Clearing(book.Currency, book.SourceSystem == "assessment" ? "Assessment" : "Migration");
                builder.AddPostingLine(book.AccountId, book.Currency, item.CurrentDelta > 0 ? "C" : "D", r.Amount);
                builder.AddPostingLine(clearing, book.Currency, item.CurrentDelta > 0 ? "D" : "C", r.Amount);
                var plan = builder.BuildPostingPlan(false);
                await c.ExecuteAsync(new CommandDefinition(
                    SqlScripts.ReadSqlScript("PostgreSql/Queries/Ledger/AccountHistoryImportStore/CreateClearingAccountForHistoryImportIfMissing"),
                    new
                    {
                        Id = clearing,
                        Owner = Guid.Empty,
                        book.Currency,
                        Country = book.CountryCode
                    },
                    tx,
                    cancellationToken: ct));
                if (!dictionary.ContainsKey(clearing))
                {
                    dictionary[clearing] = await c.QuerySingleAsync<LedgerPosition>(new CommandDefinition(
                        SqlScripts.ReadSqlScript("PostgreSql/Queries/Ledger/AccountHistoryImportStore/LockAccountForHistoryImport"),
                        new
                        {
                            Id = clearing
                        },
                        tx,
                        cancellationToken: ct));
                }

                await JournalWriter.Persist(c, tx, plan, dictionary, new Dictionary<Guid, decimal>(), item.OccurredAt.ToUniversalTime(), ct);
            }

            foreach (var refund in book.Transactions.Where(t => t.Type == "Refund" && t.Status == "Posted" && t.OriginalTransactionId is not null))
                await c.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO ledger."refunds"(
                    "originalTransactionPublicId",
                    "refundTransactionPublicId",
                    state
                )
                VALUES (
                    @Original,
                    @Id,
                    @State
                )
                """,
                new
                {
                    Original = refund.OriginalTransactionId,
                    refund.Id,
                    State = book.Transactions.Any(t => t.Type == "RefundReversal" && t.OriginalTransactionId == refund.Id) ? "Recalled" : "Active"
                },
                tx,
                cancellationToken: ct));
            foreach (var recall in book.Transactions.Where(t => t.Type == "RefundReversal" && t.Status == "Posted" && t.OriginalTransactionId is not null))
                await c.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO ledger."reversals"(
                    "originalTransactionPublicId",
                    "reversalTransactionPublicId"
                )
                VALUES (
                    @Original,
                    @Id
                )
                """,
                new
                {
                    Original = recall.OriginalTransactionId,
                    recall.Id
                },
                tx,
                cancellationToken: ct));
            foreach (var hold in book.Transactions.Where(t => t.ReservedDelta > 0))
                await c.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO ledger."externalHolds"(
                    "publicId",
                    "accountId",
                    "customerPublicId",
                    currency,
                    amount,
                    "sourceReference",
                    "createdAt"
                )
                VALUES (
                    @Id,
                    @InternalId,
                    @UserId,
                    @Currency,
                    @Amount,
                    @Reference,
                    @Created
                )
                """,
                new
                {
                    hold.Id,
                    book.AccountId,
                    source.InternalId,
                    book.UserId,
                    book.Currency,
                    Amount = hold.ReservedDelta,
                    hold.Reference,
                    Created = hold.OccurredAt.ToUniversalTime()
                },
                tx,
                cancellationToken: ct));
            source = new global::DisputePortal.Platform.Ledger.Domain.LedgerPosition(dictionary[book.AccountId])
            {
                Reserved = book.ExpectedReserved,
                Version = dictionary[book.AccountId].Version + 1
            };
            if (source.Balance != book.ExpectedBalance)
            {
                throw new InvalidOperationException("The imported financial entries do not reconcile.");
            }

            await c.ExecuteAsync(new CommandDefinition(
                """
                UPDATE ledger."accounts"
                SET
                    reserved = @Reserved,
                    version = @Version
                WHERE id = @InternalId
                """,
                source,
                tx,
                cancellationToken: ct));
            await ServiceDatabase.SaveDomainEventToOutbox(
                c,
                tx,
                DomainEvent.CreateDomainEvent(
                    "ledger",
                    "ledger.reservation.updated",
                    new BalanceSnapshot
                    {
                        AccountId = source.AccountId,
                        UserId = source.UserId,
                        Currency = source.Currency,
                        LedgerBalance = source.Balance,
                        ReservedBalance = source.Reserved,
                        Version = source.Version
                    },
                    book.SnapshotId),
                ct);
            await c.ExecuteAsync(new CommandDefinition(
                SqlScripts.ReadSqlScript("PostgreSql/Queries/Ledger/AccountHistoryImportStore/InsertAccountHistoryImportReceipt"),
                new
                {
                    book.AccountId,
                    source.InternalId,
                    book.Hash,
                    book.SnapshotId,
                    Count = book.Transactions.Length,
                    book.ExpectedBalance,
                    book.ExpectedReserved
                },
                tx,
                cancellationToken: ct));
            await ServiceDatabase.InsertServiceAuditEvent(
                c,
                tx,
                book.ActorId,
                book.SourceSystem == "assessment" ? "AssessmentTransactionsImported" : "ApprovedBookImported",
                book.AccountId,
                "Prior book hash: " + book.Hash,
                ct);
            await EnqueueSettlement(c, tx, operation, settlementEventId, book.SnapshotId, ct);
            await tx.CommitAsync(ct);
            return new(book.AccountId, book.Transactions.Length, source.Balance, source.Reserved, source.Version, false);
        }
        catch
        {
            if (beginStarted)
            {
                using var recovery = new CancellationTokenSource(db.CleanupTimeout);
                try
                {
                    await CancelIfNotCommitted(book.AccountId, attemptId, settlementEventId, recovery.Token);
                }
                catch
                {
                    // An unconfirmed local outcome retains its claim; committed attempts have a durable settlement event.
                }
            }

            throw;
        }
    }

    private static Task EnqueueSettlement(
        DbConnection connection,
        DbTransaction transaction,
        CountryOperation operation,
        Guid eventId,
        Guid snapshotId,
        CancellationToken ct)
    {
        var settlement = new global::DisputePortal.Platform.Contracts.DomainEvent(DomainEvent.CreateDomainEvent(
            "ledger",
            "ledger.import.settled",
            new CountryOperationSettlement
            {
                RequestId = operation.RequestId,
                OperationId = operation.OperationId,
                SourceKind = "Import"
            },
            snapshotId))
        {
            EventId = eventId
        };
        return ServiceDatabase.SaveDomainEventToOutbox(connection, transaction, settlement, ct);
    }

    private async Task CancelIfNotCommitted(Guid accountId, Guid attemptId, Guid settlementEventId, CancellationToken ct)
    {
        await using var connection = await db.OpenServiceDatabaseConnection(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            SELECT
                pg_advisory_xact_lock(hashtext(@Id))
            """,
            new
            {
                Id = "import:" + accountId
            },
            transaction,
            cancellationToken: ct));
        var committed = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            SqlScripts.ReadSqlScript("PostgreSql/Queries/Ledger/AccountHistoryImportStore/CheckHistoryImportSettlementEventExists"),
            new
            {
                SettlementEventId = settlementEventId
            },
            transaction,
            cancellationToken: ct));
        if (!committed)
        {
            await client.SendServiceRequest<object>(
                "configuration",
                HttpMethod.Post,
                $"/country-operations/Import/{attemptId}/settle",
                ct: ct);
        }

        await transaction.CommitAsync(ct);
    }
}
