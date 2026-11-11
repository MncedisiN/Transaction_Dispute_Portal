using System.Data.Common;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using DisputePortal.Platform.Accounts;
using DisputePortal.Platform.Audit;
using DisputePortal.Platform.Configuration;
using DisputePortal.Platform.Contracts;
using DisputePortal.Platform.ReversalReviews;
using DisputePortal.Platform.Infrastructure;
using DisputePortal.Platform.Ledger;
using DisputePortal.Platform.Ledger.Domain;
using DisputePortal.Platform.Notifications;
using DisputePortal.Platform.Payments;
using DisputePortal.Platform.Reporting;
using DisputePortal.Platform.Scheduling;
using DisputePortal.Platform.Transactions;
using Npgsql;
using Xunit;
using DisputePortal.Platform.Infrastructure.Interfaces;
using DisputePortal.Platform.Infrastructure.Models;
using DisputePortal.Platform.Notifications.Models;
using DisputePortal.Platform.Ledger.Interfaces;

namespace DisputePortal.Platform.Tests;
// An explicitly supplied native admin connection is used only to create disposable databases.
public class NativePostgreSqlTheoryAttribute : TheoryAttribute
{
    public NativePostgreSqlTheoryAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PORTAL_TEST_POSTGRESQL")))
        {
            Skip = "Set PORTAL_TEST_POSTGRESQL to run disposable native PostgreSQL integration tests.";
        }
    }
}

public class NativePostgreSqlFactAttribute : FactAttribute
{
    public NativePostgreSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PORTAL_TEST_POSTGRESQL")))
        {
            Skip = "Set PORTAL_TEST_POSTGRESQL to run disposable native PostgreSQL integration tests.";
        }
    }
}

[CollectionDefinition("Native service migrations", DisableParallelization = true)]
public class NativeServiceMigrationCollection;
[Collection("Native service migrations")]
[Trait("Provider", "PostgreSql")]
public partial class ServiceDomainMigrationTests
{
    private static PlatformSettings Settings => JsonSerializer.Deserialize<PlatformSettings>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "seed-settings.json")),
        DomainEvent.Json)!;

    [NativePostgreSqlTheory]
    [InlineData("accounts", "accounts")]
    [InlineData("transactions", "transactions")]
    [InlineData("payments", "payments")]
    [InlineData("notifications", "notifications")]
    [InlineData("audit", "audit_events")]
    [InlineData("reporting", "reported_positions")]
    [InlineData("scheduling", "scheduled_reversals")]
    [InlineData("debt", "reversal_reviews")]
    public async Task Populated_migration_preserves_original_public_identity_and_values(string service, string legacyTable)
    {
        await using var fixture = await DatabaseFixture.Create(service);
        var db = fixture.Database;
        var schema = Schema(service, db);
        await db.ApplyServiceSchemaMigrations(schema);
        var id = Guid.NewGuid();
        await using (var c = await db.OpenServiceDatabaseConnection())
        {
            await c.ExecuteAsync(
                Seed(service),
                new
                {
                    Id = id,
                    Other = Guid.NewGuid(),
                    Owner = Guid.NewGuid(),
                    At = DateTimeOffset.UtcNow,
                    Hash = new string('a', 64)
                });
        }

        var migrations = ServiceMigrations.GetServiceMigrations(service, "PostgreSql");
        await db.ApplyServiceSchemaMigrations(schema, migrations);
        await db.ApplyServiceSchemaMigrations(schema, migrations);
        await db.ValidateSchema(schema, migrations, default);
        await using var connection = await db.OpenServiceDatabaseConnection();
        var target = Target(service);
        Assert.Equal(id, await connection.ExecuteScalarAsync<Guid>($"SELECT \"publicId\" FROM {target}"));
        Assert.True(await connection.ExecuteScalarAsync<long>($"SELECT id FROM {target}") > 0);
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>($"SELECT count(*) FROM {target}"));
        Assert.Equal(
            0,
            await connection.ExecuteScalarAsync<int>(
                """
            SELECT count(*)
            FROM information_schema.tables
            WHERE table_schema='public' AND table_name=@Legacy
            """,
                new
                {
                    Legacy = legacyTable
                }));
        var stored = await connection.ExecuteScalarAsync<string>($"SELECT {PreservedColumn(service)}::text FROM {target}");
        Assert.Equal(ExpectedValue(service), stored);
        if (service is "debt" or "scheduling")
        {
            var listed = service == "debt" ? await new ReversalReviewStore(db).GetReversalReviews(default) : await new ScheduleStore(
                db,
                new SettingsClient()).GetScheduledReversalReviews(default);
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(listed, DomainEvent.Json));
            Assert.Equal(id, json.RootElement[0].GetProperty("id").GetGuid());
            Assert.False(json.RootElement[0].TryGetProperty("Id", out _));
        }

        await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync(
            $"UPDATE {target} SET \"publicId\"=@Changed",
            new { Changed = Guid.NewGuid() }));
        await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync($"UPDATE {target} SET id=id+100"));
        Assert.Equal(
            "P0001",
            (await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync($"UPDATE {target} SET id=DEFAULT"))).SqlState);
        Assert.Equal(
            migrations.Count,
            await connection.ExecuteScalarAsync<int>(
                "SELECT count(*) FROM configuration.\"serviceSchemaVersions\" WHERE \"serviceName\"=@Service",
                new
                {
                    Service = service
                }));
    }

    [NativePostgreSqlTheory]
    [InlineData("accounts")]
    [InlineData("transactions")]
    [InlineData("payments")]
    [InlineData("configuration")]
    [InlineData("ledger")]
    [InlineData("notifications")]
    [InlineData("audit")]
    [InlineData("reporting")]
    [InlineData("scheduling")]
    [InlineData("debt")]
    public async Task Fresh_schema_and_appended_version_are_idempotent_and_checksum_protected(string service)
    {
        await using var fixture = await DatabaseFixture.Create(service);
        var db = fixture.Database;
        var schema = Schema(service, db);
        var steps = ServiceMigrations.GetServiceMigrations(service, "PostgreSql");
        await db.ApplyServiceSchemaMigrations(schema, steps);
        await db.ApplyServiceSchemaMigrations(schema, steps);
        await db.ValidateSchema(schema, steps, default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.ApplyServiceSchemaMigrations(schema + "\n-- changed history", steps));
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.ApplyServiceSchemaMigrations(
            schema,
            [new ServiceMigration(steps[0].Version, steps[0].Sql + "\n-- altered")]));
        await Assert.ThrowsAsync<PostgresException>(() => db.ApplyServiceSchemaMigrations(
            schema,
            [..steps, new ServiceMigration(
                "999_invalid",
                "CREATE TABLE configuration.rollback_probe(id int); SELECT 1 FROM nonexistent_failure_probe;")]));
        await using var c = await db.OpenServiceDatabaseConnection();
        Assert.Null(await c.ExecuteScalarAsync<string?>("SELECT to_regclass('configuration.rollback_probe')::text"));
        Assert.Equal(steps.Count, await c.ExecuteScalarAsync<int>("SELECT count(*) FROM configuration.\"serviceSchemaVersions\""));
    }

    [NativePostgreSqlFact]
    public async Task Populated_posted_ledger_migrates_without_changing_balances_entries_or_outbox()
    {
        await using var fixture = await DatabaseFixture.Create("ledger");
        var db = fixture.Database;
        var schema = Schema("ledger", db);
        await db.ApplyServiceSchemaMigrations(schema);
        var account = Guid.NewGuid();
        var clearing = Guid.NewGuid();
        var journal = Guid.NewGuid();
        var transaction = Guid.NewGuid();
        var owner = Guid.NewGuid();
        await using (var c = await db.OpenServiceDatabaseConnection())
        await using (var tx = await c.BeginTransactionAsync())
        {
            await c.ExecuteAsync(
                """
                INSERT INTO ledger_accounts(account_id,user_id,currency,country_code,kind,balance,reserved)
                VALUES(@Account,@Owner,'ZAR','ZA','Customer',100,3),
                    (@Clearing,@Empty,'ZAR','ZA','Clearing',-100,0);
                INSERT INTO ledger_journals(id,transaction_id,reference,kind,request_hash,request_body,status)
                VALUES(@Journal,@Transaction,'PRESERVED-JOURNAL','ExternalCredit',@Hash,'{}','Draft');
                INSERT INTO ledger_entries(id,journal_id,account_id,currency,side,amount)
                VALUES(@Entry1,@Journal,@Account,'ZAR','C',100),
                    (@Entry2,@Journal,@Clearing,'ZAR','D',100);
                INSERT INTO ledger_reservations(transaction_id,account_id,user_id,currency,amount,usage_day,state,expires_at,reference)
                VALUES(@Reservation,@Account,@Owner,'ZAR',2,CURRENT_DATE,'Held',now()+interval '1 hour','PRESERVED-RESERVATION');
                INSERT INTO ledger_external_holds(transaction_id,account_id,user_id,currency,amount,source_reference,created_at)
                VALUES(@Hold,@Account,@Owner,'ZAR',1,'PRESERVED-HOLD',now());
                INSERT INTO ledger_book_imports(account_id,source_hash,snapshot_id,transaction_count,opening_balance,opening_reserved)
                VALUES(@Account,@Hash,@Snapshot,1,100,3);
                """,
                new
                {
                    Account = account,
                    Clearing = clearing,
                    Journal = journal,
                    Transaction = transaction,
                    Owner = owner,
                    Empty = Guid.Empty,
                    Hash = new string('a', 64),
                    Entry1 = Guid.NewGuid(),
                    Entry2 = Guid.NewGuid(),
                    Reservation = Guid.NewGuid(),
                    Hold = Guid.NewGuid(),
                    Snapshot = Guid.NewGuid()
                },
                tx);
            await ServiceDatabase.SaveDomainEventToOutbox(
                c,
                tx,
                DomainEvent.CreateDomainEvent(
                    "ledger",
                    EventTypes.LedgerTransactionPosted,
                    new LedgerPostedEvent
                    {
                        JournalId = journal,
                        TransactionId = transaction,
                        Kind = "Deposit",
                        Balances = [new global::DisputePortal.Platform.Contracts.BalanceSnapshot {
                            AccountId = account,
                            UserId = owner,
                            Currency = "ZAR",
                            LedgerBalance = 100,
                            ReservedBalance = 3,
                            Version = 1
                        }]
                    },
                    transaction),
                default);
            await c.ExecuteAsync("UPDATE ledger_journals SET status='Posted' WHERE id=@Journal", new
            {
                Journal = journal
            }, tx);
            await tx.CommitAsync();
        }

        await db.ApplyServiceSchemaMigrations(schema, ServiceMigrations.GetServiceMigrations("ledger", "PostgreSql"));
        var repository = new PostgresLedgerRepository(db, new TestFinancialConfiguration(new SettingsClient()));
        var position = await repository.ReadPosition(account, default);
        Assert.Equal(100, position.Balance);
        Assert.Equal(3, position.Reserved);
        Assert.Equal(journal, (await repository.GetJournal(transaction, default)).JournalId);
        await repository.Reconcile(default);
        await using var check = await db.OpenServiceDatabaseConnection();
        Assert.Equal(2, await check.ExecuteScalarAsync<int>("SELECT count(*) FROM ledger.entries"));
        Assert.Equal(
            1,
            await check.ExecuteScalarAsync<int>("SELECT count(*) FROM service_outbox WHERE event_type='ledger.transaction.posted'"));
        Assert.Equal(
            5,
            await check.ExecuteScalarAsync<int>("""
            SELECT count(*) FROM information_schema.table_constraints
            WHERE constraint_type='FOREIGN KEY' AND table_schema='ledger'
            """));
        Assert.Equal(
            1,
            await check.ExecuteScalarAsync<int>(
                """
            SELECT count(*) FROM ledger."bookImports" b
            JOIN ledger.accounts a ON a.id=b."accountId"
            WHERE a."publicId"=@Account AND b."openingBalance"=100 AND b."openingReserved"=3
            """,
                new
                {
                    Account = account
                }));
        await Assert.ThrowsAsync<PostgresException>(() => check.ExecuteAsync("UPDATE ledger.entries SET amount=101"));
        Assert.Equal(
            "P0001",
            (await Assert.ThrowsAsync<PostgresException>(() => check.ExecuteAsync("UPDATE ledger.reservations SET id=DEFAULT"))).SqlState);
    }

    [NativePostgreSqlFact]
    public async Task Migrated_ledger_uses_numeric_local_keys_and_retains_refund_replay_and_immutable_posting()
    {
        await using var fixture = await DatabaseFixture.Create("ledger");
        var db = fixture.Database;
        var schema = Schema("ledger", db);
        await db.ApplyServiceSchemaMigrations(schema, ServiceMigrations.GetServiceMigrations("ledger", "PostgreSql"));
        var owner = Guid.NewGuid();
        var account = Guid.NewGuid();
        var debit = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var book = new AccountHistorySnapshot
        {
            SnapshotId = Guid.NewGuid(),
            ActorId = Guid.NewGuid(),
            SourceSystem = "dispute-portal-sql",
            CapturedAt = now,
            AccountId = account,
            UserId = owner,
            Number = "1234567890",
            AccountType = "Cheque",
            CountryCode = "ZA",
            Currency = "ZAR",
            CreatedAt = now,
            ExpectedBalance = 900,
            ExpectedReserved = 3,
            Transactions = [new global::DisputePortal.Platform.Contracts.PriorTransaction
            {
                Id = Guid.NewGuid(),
                Reference = "OPEN-CREDIT",
                OccurredAt = now.AddDays(-3),
                Description = "Opening credit",
                MerchantName = "External",
                Amount = 1000,
                Currency = "ZAR",
                Type = "Credit",
                Direction = "Credit",
                Status = "Posted",
                DisputeEligible = false,
                CurrentDelta = 1000,
                ReservedDelta = 0,
                Unknown = 0
            }, new global::DisputePortal.Platform.Contracts.PriorTransaction
            {
                Id = debit,
                Reference = "ORIGINAL-DEBIT",
                OccurredAt = now.AddDays(-2),
                Description = "Card purchase",
                MerchantName = "Merchant",
                Amount = 100,
                Currency = "ZAR",
                Type = "CardPurchase",
                Direction = "Debit",
                Status = "Posted",
                DisputeEligible = true,
                CurrentDelta = -100,
                ReservedDelta = 0,
                Unknown = 0
            }, new global::DisputePortal.Platform.Contracts.PriorTransaction
            {
                Id = Guid.NewGuid(),
                Reference = "EXTERNAL-HOLD",
                OccurredAt = now.AddDays(-1),
                Description = "Pending card purchase",
                MerchantName = "Merchant",
                Amount = 3,
                Currency = "ZAR",
                Type = "CardPurchase",
                Direction = "Debit",
                Status = "Pending",
                DisputeEligible = false,
                CurrentDelta = 0,
                ReservedDelta = 3,
                Unknown = 0
            }

            ]
        };
        book = new global::DisputePortal.Platform.Contracts.AccountHistorySnapshot(book)
        {
            Hash = book.ComputeHash()
        };
        var client = new SettingsClient();
        var imports = new AccountHistoryImportStore(db, client);
        Assert.False((await imports.Import(book, default)).Replayed);
        Assert.True((await imports.Import(book, default)).Replayed);
        var repository = new PostgresLedgerRepository(db, new TestFinancialConfiguration(client));
        var source = await repository.ReadPosition(account, default);
        Assert.Equal(900, source.Balance);
        var request = new PostJournalRequest
        {
            TransactionId = Guid.NewGuid(),
            UserId = owner,
            Kind = "Refund",
            Reference = "RF-" + Guid.NewGuid().ToString("N"),
            AccountId = account,
            RecipientAccountId = null,
            Amount = 100,
            Fee = 0,
            Currency = "ZAR",
            TargetAmount = null,
            TargetCurrency = null,
            QuoteId = null,
            OriginalTransactionId = debit
        };
        var plan = new RefundPostingStrategy().BuildPostingPlan(new global::DisputePortal.Platform.Ledger.Domain.PostingContext
        {
            Request = request,
            Source = source,
            Target = null,
            Settings = Settings
        });
        var posted = await repository.Post(plan, Settings, default);
        Assert.False(posted.Replayed);
        Assert.True((await repository.Post(plan, Settings, default)).Replayed);
        Assert.Equal(1000, (await repository.ReadPosition(account, default)).Balance);
        Assert.Equal(posted.JournalId, (await repository.GetJournal(request.TransactionId, default)).JournalId);
        await repository.Entries(account, owner, default);
        await using var c = await db.OpenServiceDatabaseConnection();
        Assert.Equal(
            0,
            await c.ExecuteScalarAsync<int>("""
            SELECT count(*) FROM ledger.entries e
            LEFT JOIN ledger.accounts a ON a.id=e."accountId"
            LEFT JOIN ledger.journals j ON j.id=e."journalId"
            WHERE a.id IS NULL OR j.id IS NULL
            """));
        Assert.Equal(6, await c.ExecuteScalarAsync<int>("SELECT count(*) FROM ledger.entries"));
        Assert.Equal(1, await c.ExecuteScalarAsync<int>("SELECT count(*) FROM ledger.refunds"));
        Assert.Equal(1, await c.ExecuteScalarAsync<int>("SELECT count(*) FROM ledger.\"bookImports\""));
        Assert.Equal(1, await c.ExecuteScalarAsync<int>("SELECT count(*) FROM ledger.\"externalHolds\""));
        await Assert.ThrowsAsync<PostgresException>(() => c.ExecuteAsync("UPDATE ledger.entries SET amount=amount+1"));
        await Assert.ThrowsAsync<PostgresException>(() => c.ExecuteAsync("DELETE FROM ledger.journals"));
        // Association rows without their own public GUID must also resist native identity regeneration.
        foreach (var table in new[]
        {
            "ledger.entries",
            "ledger.journals",
            "ledger.accounts",
            "ledger.\"bookImports\"",
            "ledger.\"externalHolds\"",
            "ledger.refunds"
        }

        )
            Assert.Equal(
            "P0001",
            (await Assert.ThrowsAsync<PostgresException>(() => c.ExecuteAsync($"UPDATE {table} SET id=DEFAULT"))).SqlState);
        Assert.Equal(
            0,
            await c.ExecuteScalarAsync<int>("""
            SELECT count(*) FROM (
                SELECT "journalId",currency FROM ledger.entries
                GROUP BY "journalId",currency
                HAVING sum(CASE WHEN side='D' THEN amount ELSE -amount END)<>0
            ) invalid
            """));
    }

    [NativePostgreSqlTheory]
    [InlineData("accounts")]
    [InlineData("transactions")]
    [InlineData("payments")]
    [InlineData("configuration")]
    [InlineData("notifications")]
    [InlineData("audit")]
    [InlineData("reporting")]
    [InlineData("scheduling")]
    [InlineData("debt")]
    public async Task Runtime_stores_read_write_and_replay_against_the_migrated_domain_schema(string service)
    {
        await using var fixture = await DatabaseFixture.Create(service);
        var db = fixture.Database;
        await db.ApplyServiceSchemaMigrations(Schema(service, db), ServiceMigrations.GetServiceMigrations(service, "PostgreSql"));
        var owner = Guid.NewGuid();
        var account = Guid.NewGuid();
        var target = Guid.NewGuid();
        var id = Guid.NewGuid();
        var payment = new PaymentEvent
        {
            PaymentId = id,
            UserId = owner,
            RequestId = Guid.NewGuid(),
            Kind = "Transfer",
            AccountId = account,
            RecipientAccountId = target,
            Amount = 10,
            State = "Authorized",
            Reference = "ISOLATED-PAYMENT",
            Currency = "ZAR"
        };
        var outcome = new TransactionEvent
        {
            TransactionId = id,
            PaymentId = id,
            UserId = owner,
            Kind = "Refund",
            Reference = "ISOLATED-REFUND",
            AccountId = account,
            RecipientAccountId = null,
            Amount = 10,
            Currency = "ZAR",
            TargetAmount = null,
            TargetCurrency = null,
            Fee = 0,
            Status = "Completed"
        };
        var completed = DomainEvent.CreateDomainEvent("transactions", EventTypes.TransactionCompleted, outcome, id);
        var ledger = DomainEvent.CreateDomainEvent(
            "ledger",
            EventTypes.LedgerTransactionPosted,
            new LedgerPostedEvent
            {
                JournalId = Guid.NewGuid(),
                TransactionId = id,
                Kind = "Transfer",
                Balances = [new global::DisputePortal.Platform.Contracts.BalanceSnapshot {
                    AccountId = account,
                    UserId = owner,
                    Currency = "ZAR",
                    LedgerBalance = 100,
                    ReservedBalance = 0,
                    Version = 1
                }, new global::DisputePortal.Platform.Contracts.BalanceSnapshot {
                    AccountId = target,
                    UserId = owner,
                    Currency = "ZAR",
                    LedgerBalance = 10,
                    ReservedBalance = 0,
                    Version = 1
                }]
            },
            id);
        switch (service)
        {
            case "accounts":
                var accounts = new AccountStore(db, new SettingsClient());
                var open = new OpenAccountRequest(Guid.NewGuid(), "Cheque", "ZA", "ZAR");
                var opened = await accounts.OpenCustomerAccount(owner, open, default);
                Assert.Equal(opened.Id, (await accounts.OpenCustomerAccount(owner, open, default)).Id);
                var activated = DomainEvent.CreateDomainEvent(
            "ledger",
            "ledger.account.created",
            new AccountEvent
            {
                AccountId = opened.Id,
                UserId = owner,
                CountryCode = "ZA",
                Currency = "ZAR",
                AccountType = "Cheque"
            });
                await accounts.Handle(activated, default);
                await accounts.Handle(activated, default);
                await accounts.Handle(
            DomainEvent.CreateDomainEvent(
                "ledger",
                EventTypes.LedgerTransactionPosted,
                new LedgerPostedEvent
                {
                    JournalId = Guid.NewGuid(),
                    TransactionId = Guid.NewGuid(),
                    Kind = "Transfer",
                    Balances = [new global::DisputePortal.Platform.Contracts.BalanceSnapshot {
                        AccountId = opened.Id,
                        UserId = owner,
                        Currency = "ZAR",
                        LedgerBalance = 120,
                        ReservedBalance = 2,
                        Version = 1
                    }]
                }),
            default);
                Assert.Equal(118, (await accounts.GetAccountById(opened.Id, default)).AvailableBalance);
                Assert.Single(await accounts.GetCustomerAccounts(owner, default));
                await accounts.FindAccountByNumber(opened.Number, default);
                break;
            case "transactions":
                var incoming = new ReceivedActivityStore(db);
                var transactions = new TransactionStore(db, new HistoryStore(db), incoming);
                var intent = DomainEvent.CreateDomainEvent("payments", EventTypes.PaymentInitiated, payment, id);
                await transactions.Accept(intent, default);
                await transactions.Accept(intent, default);
                foreach (var status in new[]
                {
                    "Validated",
                    "Processing",
                    "Completed"
                }

                )
                {
                    var work = await transactions.Claim(default);
                    Assert.NotNull(work);
                    await transactions.Transition(work, status, null, default);
                }

                await incoming.Handle(ledger, default);
                await incoming.Handle(ledger, default);
                var activity = Assert.IsType<TransactionRecord[]>(await transactions.GetCustomerTransactions(owner, default));
                Assert.Equal(2, activity.Length);
                Assert.Equal(2, activity.Select(row => row.Id).Distinct().Count());
                Assert.Equal("Completed", (await transactions.GetTransactionById(id, owner, default)).Status);
                Assert.Null(await transactions.Claim(default));
                break;
            case "payments":
                var payments = new PaymentStore(db);
                var quote = new PaymentQuote
                {
                    Id = Guid.NewGuid(),
                    UserId = owner,
                    Kind = "Transfer",
                    AccountId = account,
                    RecipientAccountId = target,
                    Amount = 10,
                    Currency = "ZAR",
                    Fee = 0,
                    TargetAmount = 10,
                    TargetCurrency = "ZAR",
                    FxQuoteId = null,
                    SettingsVersion = 1,
                    ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5)
                };
                await payments.SaveQuote(quote, default);
                var request = new PaymentRequest(Guid.NewGuid(), "Transfer", account, target, 10, QuoteId: quote.Id);
                var operation = new CountryOperation
                {
                    RequestId = request.RequestId,
                    OperationId = Guid.NewGuid(),
                    SourceKind = "Payment",
                    CustomerId = owner,
                    CountryCode = "ZA",
                    Currency = "ZAR",
                    PolicyVersion = quote.SettingsVersion
                };
                var created = await payments.InsertPaymentIntent(owner, request, operation, default);
                Assert.Equal(created.Id, (await payments.InsertPaymentIntent(owner, request, operation, default)).Id);
                Assert.Equal(created.Id, (await payments.Replay(owner, request, default))!.Id);
                await payments.Activate(created.Id, default);
                var result = new global::DisputePortal.Platform.Contracts.TransactionEvent(outcome)
                {
                    PaymentId = created.Id,
                    TransactionId = created.Id,
                    Kind = "Transfer"
                };
                var paymentCompleted = DomainEvent.CreateDomainEvent("transactions", EventTypes.TransactionCompleted, result, created.Id);
                await payments.CompletePaymentIntent(paymentCompleted, default);
                await payments.CompletePaymentIntent(paymentCompleted, default);
                Assert.Equal("Completed", (await payments.Lookup(owner, request.RequestId, default)).Status);
                Assert.Single(Assert.IsType<PaymentRecord[]>(await payments.GetCustomerPayments(owner, default)));
                Assert.True((await payments.CancelUnknown(owner, Guid.NewGuid(), default)).Cancelled);
                break;
            case "configuration":
                var settings = new SettingsStore(db, new SettingsClient());
                await settings.InitializeServiceData(default);
                var current = await settings.GetPlatformSettings(default);
                await settings.InitializeServiceData(default);
                Assert.Equal(current.Version, (await settings.GetPlatformSettings(default)).Version);
                var changed = await settings.UpdatePlatformSettings(
            new(current.Version, current, "Preserve settings through the canonical storage path."),
            owner,
            default);
                Assert.Equal(current.Version + 1, changed.Version);
                break;
            case "notifications":
                var notifications = new NotificationStore(db);
                await notifications.Request(completed, default);
                await notifications.Request(completed, default);
                await notifications.Deliver(default);
                await notifications.Deliver(default);
                var notification = Assert.Single(Assert.IsAssignableFrom<IEnumerable<NotificationItem>>(await notifications.GetCustomerNotifications(
                    owner,
                    default)));
                await notifications.MarkNotificationRead(notification.Id, owner, default);
                Assert.NotNull(Assert.Single(Assert.IsAssignableFrom<IEnumerable<NotificationItem>>(await notifications.GetCustomerNotifications(
                    owner,
                    default))).ReadAt);
                break;
            case "audit":
                var audit = new AuditStore(db);
                await audit.Append(completed, default);
                await audit.Append(completed, default);
                Assert.Equal(
            completed.EventId,
            Assert.Single(Assert.IsType<DomainEvent[]>(await audit.GetAuditEntries(id, default))).EventId);
                break;
            case "reporting":
                var reporting = new ReportingStore(db);
                await reporting.Project(ledger, default);
                await reporting.Project(ledger, default);
                var paymentEvent = DomainEvent.CreateDomainEvent(
            "payments",
            EventTypes.PaymentCompleted,
            new global::DisputePortal.Platform.Contracts.PaymentEvent(payment) { State = "Completed" },
            id);
                await reporting.Project(paymentEvent, default);
                await reporting.Project(paymentEvent, default);
                await reporting.Summary(default);
                await using (var c = await db.OpenServiceDatabaseConnection())
                {
                    Assert.Equal(2, await c.ExecuteScalarAsync<int>("SELECT count(*) FROM reporting.positions"));
                    Assert.Equal(1, await c.ExecuteScalarAsync<int>("SELECT count(*) FROM reporting.payments"));
                    Assert.Equal(1, await c.ExecuteScalarAsync<int>("SELECT count(*) FROM reporting.journals"));
                }

                break;
            case "scheduling":
                var schedules = new ScheduleStore(db, new SettingsClient());
                var requestedAt = DateTimeOffset.UtcNow;
                requestedAt = new DateTimeOffset(requestedAt.Ticks - requestedAt.Ticks % TimeSpan.TicksPerMicrosecond + 7, TimeSpan.Zero).ToOffset(TimeSpan.FromHours(2));
                var schedule = new ScheduleReversal(id, Guid.NewGuid(), "Refund", requestedAt, "Approved isolated review");
                await schedules.ScheduleReversalReview(schedule, owner, default);
                await schedules.ScheduleReversalReview(schedule, owner, default);
                Assert.Equal(requestedAt, schedule.DueAt);
                Assert.Equal(TimeSpan.FromHours(2), schedule.DueAt.Offset);
                var conflict = new ScheduleReversal(
            schedule.RequestId,
            schedule.OriginalTransactionId,
            schedule.Kind,
            schedule.DueAt,
            "Conflicting review reason");
                Assert.Equal(
            "request_conflict",
            (await Assert.ThrowsAsync<PlatformException>(() => schedules.ScheduleReversalReview(conflict, owner, default))).Code);
                await schedules.QueueDueReversalPreparations(default);
                await schedules.QueueDueReversalPreparations(default);
                await schedules.GetScheduledReversalReviews(default);
                await using (var c = await db.OpenServiceDatabaseConnection())
                    Assert.Equal(
            1,
            await c.ExecuteScalarAsync<int>("SELECT count(*) FROM service_outbox WHERE event_type='reversal.preparation.requested'"));
                break;
            case "debt":
                var debt = new ReversalReviewStore(db);
                var preparation = DomainEvent.CreateDomainEvent(
            "scheduling",
            "reversal.preparation.requested",
            new ReversalPreparation
            {
                RequestId = id,
                OriginalTransactionId = Guid.NewGuid(),
                Kind = "Refund",
                ActorId = owner
            },
            id);
                await debt.SaveReversalReviewRequest(preparation, default);
                await debt.SaveReversalReviewRequest(preparation, default);
                var review = await debt.ClaimNextReversalReview(default);
                Assert.NotNull(review);
                await debt.CompleteReversalReview(review, "Requested", "transaction_requested", default);
                await debt.CompleteReversalReview(review, "Requested", "transaction_requested", default);
                Assert.Null(await debt.ClaimNextReversalReview(default));
                await debt.GetReversalReviews(default);
                break;
        }
    }

    [NativePostgreSqlFact]
    public async Task Configuration_retirement_preserves_saved_metadata_and_archives_existing_rate_values()
    {
        await using var fixture = await DatabaseFixture.Create("configuration");
        var database = fixture.Database;
        var schema = Schema("configuration", database);
        await database.ApplyServiceSchemaMigrations(schema);
        var original = new global::DisputePortal.Platform.Contracts.PlatformSettings(Settings)
        {
            Version = 7
        };
        var quoteId = Guid.NewGuid();
        var rateId = Guid.NewGuid();
        await using (var connection = await database.OpenServiceDatabaseConnection())
        {
            await connection.ExecuteAsync(
                """
                INSERT INTO platform_settings(id, version, body)
                VALUES(1, 7, CAST(@Body AS jsonb) || '{"exchangeProviders":[],"exchangePairs":[]}'::jsonb);
                INSERT INTO exchange_quotes(id, base_currency, counter_currency, rate, quoted_at, expires_at, provider)
                VALUES(@Quote, 'ZAR', 'USD', 0.062345678901, now(), now()+interval '1 hour', 'Retired test cache');
                INSERT INTO configuration."exchangeRates"(
                    "publicId", "sourceCurrency", "targetCurrency", rate, source, "sourceKind", "publicationPrecision",
                    "retrievedAt", "effectiveAt", "expiresAt", "approvalStatus", reason, version)
                VALUES(@Rate, 'ZAR', 'USD', 0.062345678901, 'Historical feed', 'Provider', 'Instant',
                    now(), now(), now()+interval '1 hour', 'Approved', 'Preserve accepted historical terms.', 1);
                """,
                new
                {
                    Body = JsonSerializer.Serialize(original, DomainEvent.Json),
                    Quote = quoteId,
                    Rate = rateId
                });
        }

        var steps = ServiceMigrations.GetServiceMigrations("configuration", "PostgreSql");
        await database.ApplyServiceSchemaMigrations(schema, [steps[0]]);
        await using (var connection = await database.OpenServiceDatabaseConnection())
            Assert.Equal(quoteId, await connection.ExecuteScalarAsync<Guid>("SELECT \"publicId\" FROM configuration.\"exchangeQuotes\""));
        await database.ApplyServiceSchemaMigrations(schema, steps);
        var settings = new SettingsStore(database, new SettingsClient());
        var migrated = await settings.GetPlatformSettings(default);
        Assert.Equal(8, migrated.Version);
        Assert.Equivalent(original.Branding, migrated.Branding, strict: true);
        Assert.Equivalent(original.Registration, migrated.Registration, strict: true);
        Assert.Equal("ZAR", migrated.Countries.Single(country => country.Code == "ZA").DefaultCurrency);
        Assert.Equal("ZWG", migrated.Countries.Single(country => country.Code == "ZW").DefaultCurrency);
        Assert.Equal("ZiG", migrated.Currencies.Single(currency => currency.Code == "ZWG").Name);
        Assert.Equal(
            original.Currencies.Single(currency => currency.Code == "USD").Decimals,
            migrated.Currencies.Single(currency => currency.Code == "USD").Decimals);
        await using var check = await database.OpenServiceDatabaseConnection();
        Assert.Null(await check.ExecuteScalarAsync<string?>("SELECT to_regclass('configuration.\"exchangeQuotes\"')::text"));
        Assert.Null(await check.ExecuteScalarAsync<string?>("SELECT to_regclass('configuration.\"exchangeRates\"')::text"));
        Assert.Equal(quoteId, await check.ExecuteScalarAsync<Guid>("SELECT \"publicId\" FROM configuration.\"historicalExchangeQuotes\""));
        Assert.Equal(rateId, await check.ExecuteScalarAsync<Guid>("SELECT \"publicId\" FROM configuration.\"historicalExchangeRates\""));
        Assert.Equal(
            0.062345678901m,
            await check.ExecuteScalarAsync<decimal>("SELECT rate FROM configuration.\"historicalExchangeQuotes\""));
        Assert.Equal(0.062345678901m, await check.ExecuteScalarAsync<decimal>("SELECT rate FROM configuration.\"historicalExchangeRates\""));
        Assert.Equal(
            "Retired test cache",
            await check.ExecuteScalarAsync<string>("SELECT provider FROM configuration.\"historicalExchangeQuotes\""));
        Assert.Equal(
            "Historical feed",
            await check.ExecuteScalarAsync<string>("SELECT source FROM configuration.\"historicalExchangeRates\""));
        Assert.False(await check.ExecuteScalarAsync<bool>("SELECT body ? 'exchangeProviders' FROM configuration.\"settings\" ORDER BY version DESC LIMIT 1"));
    }

    [NativePostgreSqlFact]
    public async Task Native_account_creation_activation_and_registration_use_only_the_saved_default()
    {
        await using var configurationFixture = await DatabaseFixture.Create("configuration");
        var configuration = await NativeConfigurationClient.Create(configurationFixture.Database, "accounts");
        await using var accountsFixture = await DatabaseFixture.Create("accounts");
        var database = accountsFixture.Database;
        await database.ApplyServiceSchemaMigrations(
            Schema("accounts", database),
            ServiceMigrations.GetServiceMigrations("accounts", "PostgreSql"));
        var store = new AccountStore(database, configuration);
        configuration.Probes["accounts"] = store.CountryEligibility;
        var owner = Guid.NewGuid();
        Assert.Equal(
            "currency_unavailable",
            (await Assert.ThrowsAsync<PlatformException>(() => store.OpenCustomerAccount(
            owner,
            new(Guid.NewGuid(), "Cheque", "ZA", "USD"),
            default))).Code);
        Assert.Equal(
            "currency_unavailable",
            (await Assert.ThrowsAsync<PlatformException>(() => store.OpenCustomerAccount(
            owner,
            new(Guid.NewGuid(), "Cheque", "ZW", "ZWG"),
            default))).Code);
        var request = new OpenAccountRequest(Guid.NewGuid(), "Cheque", "", "");
        var concurrent = await Task.WhenAll(
            store.OpenCustomerAccount(owner, request, default),
            store.OpenCustomerAccount(owner, request, default));
        Assert.Equal(concurrent[0].Id, concurrent[1].Id);
        var account = Assert.Single(await store.GetCustomerAccounts(owner, default));
        Assert.Equal("ZA", account.CountryCode);
        Assert.Equal("ZAR", account.Currency);
        Assert.Equal("PendingLedger", account.Status);
        var created = new AccountEvent
        {
            AccountId = account.Id,
            UserId = owner,
            CountryCode = "ZA",
            Currency = "ZAR",
            AccountType = account.Type
        };
        var activation = DomainEvent.CreateDomainEvent("ledger", "ledger.account.created", created);
        await store.Handle(activation, default);
        await store.Handle(activation, default);
        Assert.Equal("Active", (await store.GetAccountById(account.Id, default)).Status);
        await configuration.Operations.CompleteCountryOperation(
            DomainEvent.CreateDomainEvent("accounts", EventTypes.AccountActivated, created),
            default);
        var before = await configuration.Settings.GetPlatformSettings(default);
        var newer = await configuration.Settings.UpdatePlatformSettings(
            new(before.Version, before, "Advance the captured policy metadata."),
            owner,
            default);
        await store.ApplyCountryPolicy(newer, default);
        await store.ApplyCountryPolicy(
            new global::DisputePortal.Platform.Contracts.PlatformSettings(before) { OperatingCountry = "ZW" },
            default);
        var stable = await store.GetAccountById(account.Id, default);
        Assert.Equal("Active", stable.Status);
        Assert.Equal(newer.Version, stable.CountryPolicyVersion);
        var now = DateTimeOffset.UtcNow;
        var legacy = new AccountHistorySnapshot
        {
            SnapshotId = Guid.NewGuid(),
            ActorId = owner,
            SourceSystem = "dispute-portal-sql",
            CapturedAt = now,
            AccountId = Guid.NewGuid(),
            UserId = owner,
            Number = "100000000009",
            AccountType = "Savings",
            CountryCode = "ZA",
            Currency = "USD",
            CreatedAt = now,
            ExpectedBalance = 25.50m,
            ExpectedReserved = 0,
            Transactions = [new global::DisputePortal.Platform.Contracts.PriorTransaction
            {
                Id = Guid.NewGuid(),
                Reference = "HISTORICAL-USD-CREDIT",
                OccurredAt = now,
                Description = "Opening credit",
                MerchantName = "External",
                Amount = 25.50m,
                Currency = "USD",
                Type = "Credit",
                Direction = "Credit",
                Status = "Posted",
                DisputeEligible = false,
                CurrentDelta = 25.50m,
                ReservedDelta = 0,
                Unknown = 0
            }

            ]
        };
        legacy = new global::DisputePortal.Platform.Contracts.AccountHistorySnapshot(legacy)
        {
            Hash = legacy.ComputeHash()
        };
        await new AccountImportStore(database, configuration).Import(legacy, default);
        await store.Handle(
            DomainEvent.CreateDomainEvent(
                "ledger",
                EventTypes.LedgerTransactionPosted,
                new LedgerPostedEvent
                {
                    JournalId = Guid.NewGuid(),
                    TransactionId = Guid.NewGuid(),
                    Kind = "ExternalCredit",
                    Balances = [new global::DisputePortal.Platform.Contracts.BalanceSnapshot {
                        AccountId = legacy.AccountId,
                        UserId = owner,
                        Currency = "USD",
                        LedgerBalance = 25.50m,
                        ReservedBalance = 0,
                        Version = 1
                    }]
                }),
            default);
        await store.Handle(
            DomainEvent.CreateDomainEvent(
                "ledger",
                "ledger.account.created",
                new AccountEvent
                {
                    AccountId = legacy.AccountId,
                    UserId = owner,
                    CountryCode = "ZA",
                    Currency = "USD",
                    AccountType = "Savings"
                }),
            default);
        var blocked = await store.GetAccountById(legacy.AccountId, default);
        Assert.Equal("Blocked", blocked.Status);
        Assert.Equal("USD", blocked.Currency);
        Assert.Equal(25.50m, blocked.LedgerBalance);
        Assert.Equal(legacy.AccountId, blocked.Id);
        Assert.Equal(
            "account_missing",
            (await Assert.ThrowsAsync<PlatformException>(() => store.FindAccountByNumber(legacy.Number, default))).Code);
        var registeredOwner = Guid.NewGuid();
        await store.Handle(
            DomainEvent.CreateDomainEvent(
                "identity",
                EventTypes.UserRegistered,
                new UserRegisteredEvent
                {
                    UserId = registeredOwner,
                    CountryCode = "ZW",
                    AccountCurrency = "USD"
                }),
            default);
        var registered = Assert.Single(await store.GetCustomerAccounts(registeredOwner, default));
        Assert.Equal("ZA", registered.CountryCode);
        Assert.Equal("ZAR", registered.Currency);
    }

    [NativePostgreSqlTheory]
    [InlineData("ZAR", "Active")]
    [InlineData("USD", "Blocked")]
    public async Task Native_historical_books_and_refunds_keep_original_values_when_currency_is_quarantined(string currency, string status)
    {
        await using var configurationFixture = await DatabaseFixture.Create("configuration");
        var configuration = await NativeConfigurationClient.Create(configurationFixture.Database, "ledger");
        await using var ledgerFixture = await DatabaseFixture.Create("ledger");
        var database = ledgerFixture.Database;
        await database.ApplyServiceSchemaMigrations(
            Schema("ledger", database),
            ServiceMigrations.GetServiceMigrations("ledger", "PostgreSql"));
        var owner = Guid.NewGuid();
        var account = Guid.NewGuid();
        var debit = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var book = new AccountHistorySnapshot
        {
            SnapshotId = Guid.NewGuid(),
            ActorId = owner,
            SourceSystem = "dispute-portal-sql",
            CapturedAt = now,
            AccountId = account,
            UserId = owner,
            Number = "100000000001",
            AccountType = "Cheque",
            CountryCode = "ZA",
            Currency = currency,
            CreatedAt = now,
            ExpectedBalance = 90.25m,
            ExpectedReserved = 1.25m,
            Transactions = [new global::DisputePortal.Platform.Contracts.PriorTransaction
            {
                Id = Guid.NewGuid(),
                Reference = "ORIGINAL-OPENING-CREDIT",
                OccurredAt = now.AddDays(-3),
                Description = "Opening credit",
                MerchantName = "External",
                Amount = 100.25m,
                Currency = currency,
                Type = "Credit",
                Direction = "Credit",
                Status = "Posted",
                DisputeEligible = false,
                CurrentDelta = 100.25m,
                ReservedDelta = 0,
                Unknown = 0
            }, new global::DisputePortal.Platform.Contracts.PriorTransaction
            {
                Id = debit,
                Reference = "ORIGINAL-CARD-DEBIT",
                OccurredAt = now.AddDays(-2),
                Description = "Card purchase",
                MerchantName = "Merchant",
                Amount = 10,
                Currency = currency,
                Type = "CardPurchase",
                Direction = "Debit",
                Status = "Posted",
                DisputeEligible = true,
                CurrentDelta = -10,
                ReservedDelta = 0,
                Unknown = 0
            }, new global::DisputePortal.Platform.Contracts.PriorTransaction
            {
                Id = Guid.NewGuid(),
                Reference = "ORIGINAL-PENDING-HOLD",
                OccurredAt = now.AddDays(-1),
                Description = "Pending purchase",
                MerchantName = "Merchant",
                Amount = 1.25m,
                Currency = currency,
                Type = "CardPurchase",
                Direction = "Debit",
                Status = "Pending",
                DisputeEligible = false,
                CurrentDelta = 0,
                ReservedDelta = 1.25m,
                Unknown = 0
            }

            ]
        };
        book = new global::DisputePortal.Platform.Contracts.AccountHistorySnapshot(book)
        {
            Hash = book.ComputeHash()
        };
        var importer = new AccountHistoryImportStore(database, configuration);
        Assert.False((await importer.Import(book, default)).Replayed);
        Assert.True((await importer.Import(book, default)).Replayed);
        var policy = new RemoteFinancialConfiguration(configuration);
        var repository = new PostgresLedgerRepository(database, policy);
        var imported = await repository.ReadPosition(account, default);
        Assert.Equal(status, imported.Status);
        Assert.Equal(currency, imported.Currency);
        Assert.Equal(90.25m, imported.Balance);
        Assert.Equal(1.25m, imported.Reserved);
        await using var connection = await database.OpenServiceDatabaseConnection();
        var originalBody = await connection.ExecuteScalarAsync<string>(
            """
            SELECT "requestBody"::text
            FROM ledger.journals
            WHERE "transactionPublicId"=@Debit
            """,
            new
            {
                Debit = debit
            });
        var request = new PostJournalRequest
        {
            TransactionId = Guid.NewGuid(),
            UserId = owner,
            Kind = "Refund",
            Reference = "REFUND-" + Guid.NewGuid().ToString("N"),
            AccountId = account,
            RecipientAccountId = null,
            Amount = 10,
            Fee = 0,
            Currency = currency,
            TargetAmount = null,
            TargetCurrency = null,
            QuoteId = null,
            OriginalTransactionId = debit
        };
        var operation = await configuration.Operations.Begin(
            new CountryOperationRequest
            {
                RequestId = request.TransactionId,
                OperationId = request.TransactionId,
                SourceKind = "Reversal",
                CustomerId = owner,
                CountryCode = "ZA",
                Currency = currency,
                OriginalTransactionId = debit
            },
            "ledger",
            default);
        Assert.Null(operation.SettledAt);
        var commands = new LedgerCommandService(repository, repository, policy, new PostingPlanFactory([new RefundPostingStrategy()]));
        var result = await commands.Post(request, default);
        Assert.False(result.Replayed);
        await configuration.Operations.CompleteCountryOperation(
            DomainEvent.CreateDomainEvent(
                "transactions",
                EventTypes.TransactionCompleted,
                new TransactionEvent
                {
                    TransactionId = request.TransactionId,
                    PaymentId = request.TransactionId,
                    UserId = owner,
                    Kind = "Refund",
                    Reference = request.Reference,
                    AccountId = account,
                    RecipientAccountId = null,
                    Amount = 10,
                    Currency = currency,
                    TargetAmount = null,
                    TargetCurrency = null,
                    Fee = 0,
                    Status = "Completed"
                }),
            default);
        configuration.RejectSettingsReads = true;
        Assert.True((await commands.Post(request, default)).Replayed);
        var refunded = await repository.ReadPosition(account, default);
        Assert.Equal(status, refunded.Status);
        Assert.Equal(currency, refunded.Currency);
        Assert.Equal(100.25m, refunded.Balance);
        Assert.Equal(1.25m, refunded.Reserved);
        Assert.Equal(
            originalBody,
            await connection.ExecuteScalarAsync<string>(
                """
            SELECT "requestBody"::text
            FROM ledger.journals
            WHERE "transactionPublicId"=@Debit
            """,
                new
                {
                    Debit = debit
                }));
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM ledger.refunds"));
        Assert.Equal(
            1,
            await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM ledger.journals WHERE \"transactionPublicId\"=@Debit", new
            {
                Debit = debit
            }));
        Assert.Equal(
            0,
            await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM ledger.entries WHERE currency<>@Currency", new
            {
                Currency = currency
            }));
    }

    [NativePostgreSqlFact]
    public async Task Native_payment_claims_cancellation_replay_and_country_blockers_are_durable()
    {
        await using var configurationFixture = await DatabaseFixture.Create("configuration");
        var configuration = await NativeConfigurationClient.Create(configurationFixture.Database, "payments");
        await using var paymentsFixture = await DatabaseFixture.Create("payments");
        var database = paymentsFixture.Database;
        await database.ApplyServiceSchemaMigrations(
            Schema("payments", database),
            ServiceMigrations.GetServiceMigrations("payments", "PostgreSql"));
        var store = new PaymentStore(database);
        configuration.Probes["payments"] = store.CountryEligibility;
        var owner = Guid.NewGuid();
        var settings = await configuration.Settings.GetPlatformSettings(default);
        var quote = new PaymentQuote
        {
            Id = Guid.NewGuid(),
            UserId = owner,
            Kind = "Transfer",
            AccountId = Guid.NewGuid(),
            RecipientAccountId = Guid.NewGuid(),
            Amount = 10,
            Currency = "ZAR",
            Fee = 0,
            TargetAmount = 10,
            TargetCurrency = "ZAR",
            FxQuoteId = null,
            SettingsVersion = settings.Version,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5)
        };
        await store.SaveQuote(quote, default);
        var cancelled = new PaymentRequest(Guid.NewGuid(), "Transfer", quote.AccountId, quote.RecipientAccountId, 10, QuoteId: quote.Id);
        Assert.True((await store.CancelUnknown(owner, cancelled.RequestId, default)).Cancelled);
        await configuration.Operations.Settle("Payment", cancelled.RequestId, "payments", default, owner);
        var late = await configuration.Operations.Begin(PaymentOperation(cancelled, owner, settings.Version), "payments", default);
        Assert.NotNull(late.SettledAt);
        Assert.Equal(
            "request_cancelled",
            (await Assert.ThrowsAsync<PlatformException>(() => store.InsertPaymentIntent(owner, cancelled, late, default))).Code);
        var request = new PaymentRequest(Guid.NewGuid(), "Transfer", quote.AccountId, quote.RecipientAccountId, 10, QuoteId: quote.Id);
        var claims = await Task.WhenAll(
            configuration.Operations.Begin(PaymentOperation(request, owner, settings.Version), "payments", default),
            configuration.Operations.Begin(PaymentOperation(request, owner, settings.Version), "payments", default));
        Assert.Equal(claims[0].OperationId, claims[1].OperationId);
        var payments = await Task.WhenAll(
            store.InsertPaymentIntent(owner, request, claims[0], default),
            store.InsertPaymentIntent(owner, request, claims[1], default));
        Assert.Equal(payments[0].Id, payments[1].Id);
        Assert.Equal(claims[0].OperationId, payments[0].Id);
        var anotherOwner = Guid.NewGuid();
        Assert.Equal(
            "request_conflict",
            (await Assert.ThrowsAsync<PlatformException>(() => configuration.Operations.Begin(
            PaymentOperation(request, anotherOwner, settings.Version),
            "payments",
            default))).Code);
        await configuration.Operations.Settle("Payment", request.RequestId, "payments", default, anotherOwner);
        Assert.Null((await configuration.Operations.GetCountryOperation(claims[0].OperationId, default)).SettledAt);
        Assert.Equal(
            "request_conflict",
            (await Assert.ThrowsAsync<PlatformException>(() => store.CancelUnknown(anotherOwner, request.RequestId, default))).Code);
        await AssertCountryBlockedUnchanged(configuration.Settings, owner);
        // Even if a trusted operator settles the registry capture, the saved unfinished payment independently blocks a switch.
        await configuration.Operations.Settle("Payment", request.RequestId, "payments", default, owner);
        await AssertCountryBlockedUnchanged(configuration.Settings, owner);
        await store.Activate(payments[0].Id, default);
        var completed = DomainEvent.CreateDomainEvent(
            "transactions",
            EventTypes.TransactionCompleted,
            new TransactionEvent
            {
                TransactionId = payments[0].Id,
                PaymentId = payments[0].Id,
                UserId = owner,
                Kind = "Transfer",
                Reference = payments[0].Reference,
                AccountId = quote.AccountId,
                RecipientAccountId = quote.RecipientAccountId,
                Amount = 10,
                Currency = "ZAR",
                TargetAmount = 10,
                TargetCurrency = "ZAR",
                Fee = 0,
                Status = "Completed"
            });
        await store.CompletePaymentIntent(completed, default);
        await store.CompletePaymentIntent(completed, default);
        await configuration.Operations.CompleteCountryOperation(completed, default);
        Assert.False((await store.CountryEligibility(default)).HasUnfinishedOperations);
        var replay = await store.Replay(owner, request, default);
        Assert.Equal(payments[0].Id, replay!.Id);
        Assert.Equal("Completed", replay.Status);
        var changed = await configuration.Settings.UpdatePlatformSettings(
            new(
                settings.Version,
                new global::DisputePortal.Platform.Contracts.PlatformSettings(settings) { OperatingCountry = "ZW" },
                "Switch only after all saved operations finish."),
            owner,
            default);
        Assert.Equal("ZW", changed.OperatingCountry);
        Assert.Equal("ZWG", CountryCurrencyPolicy.Current(changed).DefaultCurrency);
        Assert.Equal("ZAR", (await store.Lookup(owner, request.RequestId, default)).Currency);
        await using var connection = await database.OpenServiceDatabaseConnection();
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM payments.payments"));
    }

    [NativePostgreSqlFact]
    public async Task Native_settled_claim_without_a_local_payment_cannot_create_a_new_financial_intent()
    {
        await using var configurationFixture = await DatabaseFixture.Create("configuration");
        var configuration = await NativeConfigurationClient.Create(configurationFixture.Database, "payments");
        await using var paymentsFixture = await DatabaseFixture.Create("payments");
        var database = paymentsFixture.Database;
        await database.ApplyServiceSchemaMigrations(
            Schema("payments", database),
            ServiceMigrations.GetServiceMigrations("payments", "PostgreSql"));
        var store = new PaymentStore(database);
        var owner = Guid.NewGuid();
        var settings = await configuration.Settings.GetPlatformSettings(default);
        var quote = new PaymentQuote
        {
            Id = Guid.NewGuid(),
            UserId = owner,
            Kind = "Transfer",
            AccountId = Guid.NewGuid(),
            RecipientAccountId = Guid.NewGuid(),
            Amount = 10,
            Currency = "ZAR",
            Fee = 0,
            TargetAmount = 10,
            TargetCurrency = "ZAR",
            FxQuoteId = null,
            SettingsVersion = settings.Version,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5)
        };
        var request = new PaymentRequest(Guid.NewGuid(), "Transfer", quote.AccountId, quote.RecipientAccountId, 10, QuoteId: quote.Id);
        await store.SaveQuote(quote, default);
        var claim = await configuration.Operations.Begin(PaymentOperation(request, owner, settings.Version), "payments", default);
        await configuration.Operations.Settle("Payment", request.RequestId, "payments", default, owner);
        var settled = await configuration.Operations.GetCountryOperation(claim.OperationId, default);
        Assert.Equal(
            "country_operation_required",
            (await Assert.ThrowsAsync<PlatformException>(() => store.InsertPaymentIntent(owner, request, settled, default))).Code);
        await using var connection = await database.OpenServiceDatabaseConnection();
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM payments.payments"));
    }

    [NativePostgreSqlFact]
    public async Task Native_country_update_and_operation_begin_serialize_without_partial_settings_changes()
    {
        await using var fixture = await DatabaseFixture.Create("configuration");
        var configuration = await NativeConfigurationClient.Create(fixture.Database, "payments");
        var settings = await configuration.Settings.GetPlatformSettings(default);
        var owner = Guid.NewGuid();
        var request = new CountryOperationRequest
        {
            RequestId = Guid.NewGuid(),
            SourceKind = "Payment",
            CustomerId = owner,
            CountryCode = "ZA",
            Currency = "ZAR",
            ExpectedVersion = settings.Version
        };
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<string> Begin()
        {
            await ready.Task;
            try
            {
                await configuration.Operations.Begin(request, "payments", default);
                return "Begun";
            }
            catch (PlatformException error)
            {
                return error.Code;
            }
        }

        async Task<string> Change()
        {
            await ready.Task;
            try
            {
                await configuration.Settings.UpdatePlatformSettings(
                    new(
                        settings.Version,
                        new global::DisputePortal.Platform.Contracts.PlatformSettings(settings) { OperatingCountry = "ZW" },
                        "Concurrent country switch."),
                    owner,
                    default);
                return "Changed";
            }
            catch (PlatformException error)
            {
                return error.Code;
            }
        }

        var begin = Begin();
        var change = Change();
        ready.SetResult();
        var outcomes = await Task.WhenAll(begin, change);
        var saved = await configuration.Settings.GetPlatformSettings(default);
        if (outcomes[0] == "Begun")
        {
            Assert.Equal("country_change_blocked", outcomes[1]);
            Assert.Equal(
                JsonSerializer.Serialize(
                    new global::DisputePortal.Platform.Contracts.PlatformSettings(settings) { Publication = null },
                    DomainEvent.Json),
                JsonSerializer.Serialize(
                    new global::DisputePortal.Platform.Contracts.PlatformSettings(saved) { Publication = null },
                    DomainEvent.Json));
        }
        else
        {
            Assert.Equal("settings_changed", outcomes[0]);
            Assert.Equal("Changed", outcomes[1]);
            Assert.Equal(settings.Version + 1, saved.Version);
            Assert.Equal("ZW", saved.OperatingCountry);
        }
    }

    [NativePostgreSqlFact]
    public async Task Native_preparing_failure_is_recoverable_and_publishes_a_terminal_event_once_after_cleanup()
    {
        await using var fixture = await DatabaseFixture.Create("payments");
        var database = fixture.Database;
        await database.ApplyServiceSchemaMigrations(
            Schema("payments", database),
            ServiceMigrations.GetServiceMigrations("payments", "PostgreSql"));
        var store = new PaymentStore(database);
        var owner = Guid.NewGuid();
        var quote = new PaymentQuote
        {
            Id = Guid.NewGuid(),
            UserId = owner,
            Kind = "Transfer",
            AccountId = Guid.NewGuid(),
            RecipientAccountId = Guid.NewGuid(),
            Amount = 10,
            Currency = "ZAR",
            Fee = 0,
            TargetAmount = 10,
            TargetCurrency = "ZAR",
            FxQuoteId = null,
            SettingsVersion = 1,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5)
        };
        var request = new PaymentRequest(Guid.NewGuid(), "Transfer", quote.AccountId, quote.RecipientAccountId, 10, QuoteId: quote.Id);
        await store.SaveQuote(quote, default);
        var operation = new CountryOperation
        {
            RequestId = request.RequestId,
            OperationId = Guid.NewGuid(),
            SourceKind = "Payment",
            CustomerId = owner,
            CountryCode = "ZA",
            Currency = "ZAR",
            PolicyVersion = 1
        };
        var payment = await store.InsertPaymentIntent(owner, request, operation, default);
        Assert.True(await store.Fail(payment.Id, "insufficient_funds", default));
        Assert.Equal("Failed", (await store.Lookup(owner, request.RequestId, default)).Status);
        Assert.True((await store.CountryEligibility(default)).HasUnfinishedOperations);
        Assert.Equal(payment.Id, Assert.Single(await store.Preparing(default)).Id);
        await using var connection = await database.OpenServiceDatabaseConnection();
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM payments.\"cancelledRequests\""));
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM service_outbox WHERE event_type='payment.failed'"));
        // The service calls this only after the ledger release acknowledgement; its ordering is covered by the HTTP-client flow tests.
        await store.FinishFailure(payment.Id, default);
        await store.FinishFailure(payment.Id, default);
        Assert.False((await store.CountryEligibility(default)).HasUnfinishedOperations);
        Assert.Empty(await store.Preparing(default));
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM service_outbox WHERE event_type='payment.failed'"));
        Assert.Equal(payment.Id, (await store.InsertPaymentIntent(owner, request, operation, default)).Id);
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM payments.payments"));
    }

    [NativePostgreSqlFact]
    public async Task Native_distinct_books_can_share_a_snapshot_without_sharing_import_claims()
    {
        await using var configurationFixture = await DatabaseFixture.Create("configuration");
        var configuration = await NativeConfigurationClient.Create(configurationFixture.Database, "ledger");
        await using var ledgerFixture = await DatabaseFixture.Create("ledger");
        var database = ledgerFixture.Database;
        await database.ApplyServiceSchemaMigrations(
            Schema("ledger", database),
            ServiceMigrations.GetServiceMigrations("ledger", "PostgreSql"));
        var snapshot = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var first = ImportBook(snapshot, owner, "100000000031", 11.25m);
        var second = ImportBook(snapshot, owner, "100000000032", 22.50m);
        var store = new AccountHistoryImportStore(database, configuration);
        var results = await Task.WhenAll(store.Import(first, default), store.Import(second, default));
        Assert.All(results, result => Assert.False(result.Replayed));
        var settlements = await ImportSettlements(database);
        Assert.Equal(2, settlements.Length);
        var captures = settlements.Select(value => value.DeserializePayload<CountryOperationSettlement>()).ToArray();
        Assert.Equal(2, captures.Select(value => value.RequestId).Distinct().Count());
        Assert.Equal(2, captures.Select(value => value.OperationId).Distinct().Count());
        Assert.All(captures, value => Assert.NotEqual(snapshot, value.RequestId));
        await using var connection = await database.OpenServiceDatabaseConnection();
        Assert.Equal(
            2,
            await connection.ExecuteScalarAsync<int>(
                "SELECT count(*) FROM ledger.\"bookImports\" WHERE \"snapshotPublicId\"=@Snapshot",
                new
                {
                    Snapshot = snapshot
                }));
        Assert.Equal(
            11.25m,
            await connection.ExecuteScalarAsync<decimal>("SELECT balance FROM ledger.accounts WHERE \"publicId\"=@Id", new
            {
                Id = first.AccountId
            }));
        Assert.Equal(
            22.50m,
            await connection.ExecuteScalarAsync<decimal>("SELECT balance FROM ledger.accounts WHERE \"publicId\"=@Id", new
            {
                Id = second.AccountId
            }));
        foreach (var settlement in settlements)
            await configuration.Operations.CompleteCountryOperation(settlement, default);
        await using var registry = await configurationFixture.Database.OpenServiceDatabaseConnection();
        Assert.Equal(
            0,
            await registry.ExecuteScalarAsync<int>("SELECT count(*) FROM configuration.\"countryOperations\" WHERE \"settledAt\" IS NULL"));
    }

    [NativePostgreSqlFact]
    public async Task Native_confirmed_import_rollback_settles_only_its_attempt_and_allows_an_exact_book_retry()
    {
        await using var configurationFixture = await DatabaseFixture.Create("configuration");
        var configuration = await NativeConfigurationClient.Create(configurationFixture.Database, "ledger");
        await using var ledgerFixture = await DatabaseFixture.Create("ledger");
        var database = ledgerFixture.Database;
        await database.ApplyServiceSchemaMigrations(
            Schema("ledger", database),
            ServiceMigrations.GetServiceMigrations("ledger", "PostgreSql"));
        var book = ImportBook(Guid.NewGuid(), Guid.NewGuid(), "100000000033", 33.75m);
        var store = new AccountHistoryImportStore(database, configuration);
        configuration.NextOperationReadFailure = new IOException("The registry acknowledgement was interrupted before any local write.");
        await Assert.ThrowsAsync<IOException>(() => store.Import(book, default));
        await using var ledger = await database.OpenServiceDatabaseConnection();
        await using var registry = await configurationFixture.Database.OpenServiceDatabaseConnection();
        Assert.Equal(0, await ledger.ExecuteScalarAsync<int>("SELECT count(*) FROM ledger.\"bookImports\""));
        Assert.Empty(await ImportSettlements(database));
        Assert.Equal(
            1,
            await registry.ExecuteScalarAsync<int>("SELECT count(*) FROM configuration.\"countryOperations\" WHERE \"settledAt\" IS NOT NULL"));
        Assert.False((await store.Import(book, default)).Replayed);
        var settlement = Assert.Single(await ImportSettlements(database));
        Assert.Equal(2, await registry.ExecuteScalarAsync<int>("SELECT count(*) FROM configuration.\"countryOperations\""));
        Assert.Equal(
            1,
            await registry.ExecuteScalarAsync<int>("SELECT count(*) FROM configuration.\"countryOperations\" WHERE \"settledAt\" IS NULL"));
        await configuration.Operations.CompleteCountryOperation(settlement, default);
        Assert.Equal(
            0,
            await registry.ExecuteScalarAsync<int>("SELECT count(*) FROM configuration.\"countryOperations\" WHERE \"settledAt\" IS NULL"));
        Assert.Equal(
            33.75m,
            await ledger.ExecuteScalarAsync<decimal>("SELECT balance FROM ledger.accounts WHERE \"publicId\"=@Id", new
            {
                Id = book.AccountId
            }));
    }

    [NativePostgreSqlFact]
    public async Task Native_lost_import_response_and_replay_keep_money_once_and_each_attempt_has_durable_settlement()
    {
        await using var configurationFixture = await DatabaseFixture.Create("configuration");
        var configuration = await NativeConfigurationClient.Create(configurationFixture.Database, "ledger");
        await using var ledgerFixture = await DatabaseFixture.Create("ledger");
        var database = ledgerFixture.Database;
        await database.ApplyServiceSchemaMigrations(
            Schema("ledger", database),
            ServiceMigrations.GetServiceMigrations("ledger", "PostgreSql"));
        var book = ImportBook(Guid.NewGuid(), Guid.NewGuid(), "100000000034", 44.125m);
        var store = new AccountHistoryImportStore(database, configuration);
        async Task LoseAcknowledgement()
        {
            await store.Import(book, default);
            throw new IOException("The HTTP acknowledgement was lost after the ledger committed.");
        }

        await Assert.ThrowsAsync<IOException>(LoseAcknowledgement);
        Assert.Single(await ImportSettlements(database));
        Assert.True((await store.Import(book, default)).Replayed);
        var settlements = await ImportSettlements(database);
        Assert.Equal(2, settlements.Length);
        await using var ledger = await database.OpenServiceDatabaseConnection();
        await using var registry = await configurationFixture.Database.OpenServiceDatabaseConnection();
        Assert.Equal(1, await ledger.ExecuteScalarAsync<int>("SELECT count(*) FROM ledger.\"bookImports\""));
        Assert.Equal(1, await ledger.ExecuteScalarAsync<int>("SELECT count(*) FROM ledger.journals"));
        Assert.Equal(
            44.125m,
            await ledger.ExecuteScalarAsync<decimal>("SELECT balance FROM ledger.accounts WHERE \"publicId\"=@Id", new
            {
                Id = book.AccountId
            }));
        Assert.Equal(
            2,
            await registry.ExecuteScalarAsync<int>("SELECT count(*) FROM configuration.\"countryOperations\" WHERE \"settledAt\" IS NULL"));
        await configuration.Operations.CompleteCountryOperation(settlements[0], default);
        Assert.Equal(
            1,
            await registry.ExecuteScalarAsync<int>("SELECT count(*) FROM configuration.\"countryOperations\" WHERE \"settledAt\" IS NULL"));
        await configuration.Operations.CompleteCountryOperation(settlements[0], default);
        Assert.Equal(
            1,
            await registry.ExecuteScalarAsync<int>("SELECT count(*) FROM configuration.\"countryOperations\" WHERE \"settledAt\" IS NULL"));
        await configuration.Operations.CompleteCountryOperation(settlements[1], default);
        Assert.Equal(
            0,
            await registry.ExecuteScalarAsync<int>("SELECT count(*) FROM configuration.\"countryOperations\" WHERE \"settledAt\" IS NULL"));
    }

    private static AccountHistorySnapshot ImportBook(Guid snapshot, Guid owner, string number, decimal amount)
    {
        var now = DateTimeOffset.UtcNow;
        var book = new AccountHistorySnapshot
        {
            SnapshotId = snapshot,
            ActorId = owner,
            SourceSystem = "dispute-portal-sql",
            CapturedAt = now,
            AccountId = Guid.NewGuid(),
            UserId = owner,
            Number = number,
            AccountType = "Cheque",
            CountryCode = "ZA",
            Currency = "ZAR",
            CreatedAt = now,
            ExpectedBalance = amount,
            ExpectedReserved = 0,
            Transactions = [new global::DisputePortal.Platform.Contracts.PriorTransaction
            {
                Id = Guid.NewGuid(),
                Reference = "ORIGINAL-IMPORT-CREDIT",
                OccurredAt = now.AddDays(-1),
                Description = "Opening credit",
                MerchantName = "External",
                Amount = amount,
                Currency = "ZAR",
                Type = "Credit",
                Direction = "Credit",
                Status = "Posted",
                DisputeEligible = false,
                CurrentDelta = amount,
                ReservedDelta = 0,
                Unknown = 0
            }

            ]
        };
        return new global::DisputePortal.Platform.Contracts.AccountHistorySnapshot(book)
        {
            Hash = book.ComputeHash()
        };
    }

    private static async Task<DomainEvent[]> ImportSettlements(ServiceDatabase database)
    {
        await using var connection = await database.OpenServiceDatabaseConnection();
        var bodies = await connection.QueryAsync<string>("""
            SELECT body::text
            FROM service_outbox
            WHERE event_type='ledger.import.settled'
            ORDER BY created_at, event_id
            """);
        return bodies.Select(body => JsonSerializer.Deserialize<DomainEvent>(body, DomainEvent.Json)!).ToArray();
    }

    [NativePostgreSqlTheory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task Native_existing_approval_intent_can_gain_a_blocking_claim_without_rewriting_accepted_financial_snapshots(int acceptedSettingsVersion)
    {
        await using var configurationFixture = await DatabaseFixture.Create("configuration");
        var configuration = await NativeConfigurationClient.Create(configurationFixture.Database, "payments");
        await using var paymentsFixture = await DatabaseFixture.Create("payments");
        var payments = paymentsFixture.Database;
        await payments.ApplyServiceSchemaMigrations(
            Schema("payments", payments),
            ServiceMigrations.GetServiceMigrations("payments", "PostgreSql"));
        await using var transactionsFixture = await DatabaseFixture.Create("transactions");
        var transactions = transactionsFixture.Database;
        await transactions.ApplyServiceSchemaMigrations(
            Schema("transactions", transactions),
            ServiceMigrations.GetServiceMigrations("transactions", "PostgreSql"));
        var owner = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        var businessRequestId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var acceptedAt = new DateTimeOffset(2026, 11, 1, 10, 0, 0, TimeSpan.Zero);
        var request = new PaymentRequest(businessRequestId, "Withdrawal", accountId, null, 5000, QuoteId: Guid.NewGuid());
        var accepted = new PaymentEvent
        {
            PaymentId = paymentId,
            UserId = owner,
            RequestId = businessRequestId,
            Kind = "Withdrawal",
            AccountId = accountId,
            RecipientAccountId = null,
            Amount = 5000,
            State = "AwaitingApproval",
            Reference = "ORIGINAL-PENDING-WITHDRAWAL",
            Currency = "ZAR",
            Fee = 2.75m,
            SettingsVersion = acceptedSettingsVersion,
            QuoteExpiresAt = acceptedAt.AddMinutes(5)
        };
        var body = JsonSerializer.Serialize(accepted, DomainEvent.Json);
        var requestHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
            request,
            DomainEvent.Json))));
        await using var paymentConnection = await payments.OpenServiceDatabaseConnection();
        await paymentConnection.ExecuteAsync(
            """
            INSERT INTO payments.payments(
                "publicId", "customerPublicId", "requestId", "requestHash", "eventBody", status, reference, "createdAt")
            VALUES(
                @PaymentId, @Owner, @RequestId, @RequestHash, CAST(@Body AS jsonb), 'AwaitingApproval', @Reference, @AcceptedAt)
            """,
            new
            {
                PaymentId = paymentId,
                Owner = owner,
                RequestId = businessRequestId,
                RequestHash = requestHash,
                Body = body,
                accepted.Reference,
                AcceptedAt = acceptedAt
            });
        await using var transactionConnection = await transactions.OpenServiceDatabaseConnection();
        await transactionConnection.ExecuteAsync(
            """
            INSERT INTO transactions.transactions(
                "publicId", "paymentPublicId", "customerPublicId", "paymentBody", status, attempts,
                "nextAttemptAt", "createdAt", "updatedAt")
            VALUES(
                @PaymentId, @PaymentId, @Owner, CAST(@Body AS jsonb), 'AwaitingApproval', 0,
                @AcceptedAt, @AcceptedAt, @AcceptedAt)
            """,
            new
            {
                PaymentId = paymentId,
                Owner = owner,
                Body = body,
                AcceptedAt = acceptedAt
            });
        const string paymentFingerprintSql = """
            SELECT md5(concat_ws('|',
                id::text, "publicId"::text, "customerPublicId"::text, "requestId"::text, "requestHash",
                "eventBody"::text, status, reference, "createdAt"::text, COALESCE("failureCode", '')))
            FROM payments.payments
            WHERE "publicId"=@PaymentId
            """;
        const string transactionFingerprintSql = """
            SELECT md5(concat_ws('|',
                id::text, "publicId"::text, "paymentPublicId"::text, "customerPublicId"::text, "paymentBody"::text,
                status, COALESCE("failureCode", ''), attempts::text, "nextAttemptAt"::text,
                COALESCE("leaseId"::text, ''), COALESCE("leaseUntil"::text, ''), "createdAt"::text, "updatedAt"::text))
            FROM transactions.transactions
            WHERE "publicId"=@PaymentId
            """;
        var paymentBefore = await paymentConnection.ExecuteScalarAsync<string>(paymentFingerprintSql, new
        {
            PaymentId = paymentId
        });
        var transactionBefore = await transactionConnection.ExecuteScalarAsync<string>(transactionFingerprintSql, new
        {
            PaymentId = paymentId
        });
        var settings = await configuration.Settings.GetPlatformSettings(default);
        settings = await configuration.Settings.UpdatePlatformSettings(
            new(settings.Version, settings, "Advance metadata without changing accepted financial terms."),
            owner,
            default);
        settings = await configuration.Settings.UpdatePlatformSettings(
            new(settings.Version, settings, "Advance metadata again without approving any payment."),
            owner,
            default);
        Assert.Equal(3, settings.Version);
        Assert.Equal("ZA", settings.OperatingCountry);
        Assert.Equal("ZAR", CountryCurrencyPolicy.Current(settings).DefaultCurrency);
        var savedBody = await paymentConnection.ExecuteScalarAsync<string>(
            """
            SELECT "eventBody"::text
            FROM payments.payments
            WHERE "publicId"=@PaymentId
            """,
            new
            {
                PaymentId = paymentId
            });
        var savedIntent = JsonSerializer.Deserialize<PaymentEvent>(
            savedBody ?? throw new InvalidOperationException("The preserved payment intent is missing."),
            DomainEvent.Json)!;
        var adoption = new CountryOperationRequest
        {
            RequestId = savedIntent.RequestId,
            OperationId = savedIntent.PaymentId,
            CustomerId = savedIntent.UserId,
            SourceKind = "Payment",
            CountryCode = settings.OperatingCountry,
            Currency = savedIntent.Currency,
            ExpectedVersion = settings.Version
        };
        var claim = await configuration.Operations.Begin(adoption, "payments", default);
        var replay = await configuration.Operations.Begin(adoption, "payments", default);
        Assert.Equal(businessRequestId, claim.RequestId);
        Assert.Equal(paymentId, claim.OperationId);
        Assert.NotEqual(claim.RequestId, claim.OperationId);
        Assert.Equal(owner, claim.CustomerId);
        Assert.Equal(settings.Version, claim.PolicyVersion);
        Assert.Null(claim.SettledAt);
        Assert.Equal(claim.OperationId, replay.OperationId);
        // Exercise the actual registry guard independently of service eligibility probes.
        await AssertCountryBlockedUnchanged(configuration.Settings, owner);
        Assert.Equal(
            paymentBefore,
            await paymentConnection.ExecuteScalarAsync<string>(paymentFingerprintSql, new
            {
                PaymentId = paymentId
            }));
        Assert.Equal(
            transactionBefore,
            await transactionConnection.ExecuteScalarAsync<string>(transactionFingerprintSql, new
            {
                PaymentId = paymentId
            }));
        Assert.Equal(
            requestHash,
            await paymentConnection.ExecuteScalarAsync<string>(
                "SELECT \"requestHash\" FROM payments.payments WHERE \"publicId\"=@PaymentId",
                new
                {
                    PaymentId = paymentId
                }));
        Assert.Equal(acceptedSettingsVersion, savedIntent.SettingsVersion);
        Assert.Equal(
            acceptedSettingsVersion,
            await paymentConnection.ExecuteScalarAsync<int>("SELECT COALESCE((\"eventBody\"->>'settingsVersion')::int, 0) FROM payments.payments"));
        Assert.Equal(
            acceptedSettingsVersion,
            await transactionConnection.ExecuteScalarAsync<int>("SELECT COALESCE((\"paymentBody\"->>'settingsVersion')::int, 0) FROM transactions.transactions"));
        Assert.Equal(
            acceptedSettingsVersion > 0,
            await paymentConnection.ExecuteScalarAsync<bool>("SELECT \"eventBody\" ? 'settingsVersion' FROM payments.payments"));
        Assert.Equal(
            acceptedSettingsVersion > 0,
            await transactionConnection.ExecuteScalarAsync<bool>("SELECT \"paymentBody\" ? 'settingsVersion' FROM transactions.transactions"));
        Assert.Equal("AwaitingApproval", await paymentConnection.ExecuteScalarAsync<string>("SELECT status FROM payments.payments"));
        Assert.Equal(
            "AwaitingApproval",
            await transactionConnection.ExecuteScalarAsync<string>("SELECT status FROM transactions.transactions"));
        Assert.Equal(1, await paymentConnection.ExecuteScalarAsync<int>("SELECT count(*) FROM payments.payments"));
        Assert.Equal(1, await transactionConnection.ExecuteScalarAsync<int>("SELECT count(*) FROM transactions.transactions"));
        Assert.Equal(0, await paymentConnection.ExecuteScalarAsync<int>("SELECT count(*) FROM payments.\"cancelledRequests\""));
        Assert.Equal(0, await paymentConnection.ExecuteScalarAsync<int>("SELECT count(*) FROM payments.quotes"));
        Assert.Equal(0, await paymentConnection.ExecuteScalarAsync<int>("SELECT count(*) FROM service_outbox"));
        Assert.Equal(0, await paymentConnection.ExecuteScalarAsync<int>("SELECT count(*) FROM service_audit"));
        Assert.Equal(0, await transactionConnection.ExecuteScalarAsync<int>("SELECT count(*) FROM service_outbox"));
        Assert.Equal(0, await transactionConnection.ExecuteScalarAsync<int>("SELECT count(*) FROM service_audit"));
        Assert.Equal(0, await transactionConnection.ExecuteScalarAsync<int>("SELECT attempts FROM transactions.transactions"));
        await using var registry = await configurationFixture.Database.OpenServiceDatabaseConnection();
        Assert.Equal(
            1,
            await registry.ExecuteScalarAsync<int>("SELECT count(*) FROM configuration.\"countryOperations\" WHERE \"settledAt\" IS NULL"));
        Assert.Equal(3, (await configuration.Settings.GetPlatformSettings(default)).Version);
    }

    private static CountryOperationRequest PaymentOperation(PaymentRequest request, Guid owner, int version) => new()
    {
        RequestId = request.RequestId,
        SourceKind = "Payment",
        CustomerId = owner,
        CountryCode = "ZA",
        Currency = "ZAR",
        ExpectedVersion = version
    };

    private static async Task AssertCountryBlockedUnchanged(SettingsStore settings, Guid actor)
    {
        var before = await settings.GetPlatformSettings(default);
        var error = await Assert.ThrowsAsync<PlatformException>(() => settings.UpdatePlatformSettings(
            new(
                before.Version,
                new global::DisputePortal.Platform.Contracts.PlatformSettings(before) { OperatingCountry = "ZW" },
                "Blocked switch must preserve every saved setting."),
            actor,
            default));
        Assert.Equal("country_change_blocked", error.Code);
        Assert.Equal(
            JsonSerializer.Serialize(
                new global::DisputePortal.Platform.Contracts.PlatformSettings(before) { Publication = null },
                DomainEvent.Json),
            JsonSerializer.Serialize(
                new global::DisputePortal.Platform.Contracts.PlatformSettings((await settings.GetPlatformSettings(default))) { Publication = null },
                DomainEvent.Json));
    }

    private static string Schema(string service, ServiceDatabase db) => service switch
    {
        "accounts" => AccountStore.Schema,
        "transactions" => TransactionStore.Schema + HistoryStore.Schema + ReceivedActivityStore.Schema,
        "payments" => PaymentStore.Schema,
        "configuration" => SettingsStore.Schema + ConfigurationBootstrapSchema.PostgreSql,
        "ledger" => new LedgerModule(null!, null!).Schema,
        "notifications" => NotificationStore.Schema,
        "audit" => AuditStore.Schema,
        "reporting" => ReportingStore.Schema,
        "scheduling" => ScheduleStore.Schema,
        "debt" => ReversalReviewStore.Schema,
        _ => throw new ArgumentException("Unknown isolated service.")
    };

    private static string Target(string service) => service switch
    {
        "audit" => "audit.\"auditEvents\"",
        "reporting" => "reporting.positions",
        "scheduling" => "scheduling.reversals",
        "debt" => "debt.\"reversalReviews\"",
        _ => service + "." + service
    };

    private static string PreservedColumn(string service) => service switch
    {
        "accounts" => "\"accountNumber\"",
        "payments" => "reference",
        "audit" => "\"eventType\"",
        "reporting" => "balance",
        _ => "status"
    };

    private static string ExpectedValue(string service) => service switch
    {
        "accounts" => "1234567890",
        "transactions" => "Completed",
        "payments" => "PAY-ORIGINAL",
        "notifications" => "Pending",
        "audit" => "transaction.completed",
        "reporting" => "123.45000000",
        "scheduling" => "Scheduled",
        "debt" => "Pending",
        _ => throw new ArgumentException("Unknown isolated service.")
    };

    private static string Seed(string service) => service switch
    {
        "accounts" => """
            INSERT INTO accounts(
                id,
                user_id,
                request_id,
                account_number,
                account_type,
                country_code,
                currency,
                status,
                ledger_balance,
                reserved_balance,
                balance_version
            )
            VALUES(
                @Id,
                @Owner,
                @Other,
                '1234567890',
                'Cheque',
                'ZA',
                'ZAR',
                'Active',
                123.45,
                3,
                9
            )
            """,
        "transactions" => """
            INSERT INTO transactions(
                id,
                payment_id,
                user_id,
                payment_body,
                status
            )
            VALUES(
                @Id,
                @Other,
                @Owner,
                '{}',
                'Completed'
            )
            """,
        "payments" => """
            INSERT INTO payments(
                id,
                user_id,
                request_id,
                request_hash,
                event_body,
                status,
                reference
            )
            VALUES(
                @Id,
                @Owner,
                @Other,
                @Hash,
                '{}',
                'Completed',
                'PAY-ORIGINAL'
            )
            """,
        "notifications" => """
            INSERT INTO notifications(
                id,
                user_id,
                source_event_id,
                aggregate_id,
                template_key
            )
            VALUES(
                @Id,
                @Owner,
                @Other,
                @Other,
                'dispute.refund.completed'
            )
            """,
        "audit" => """
            INSERT INTO audit_events(
                event_id,
                correlation_id,
                event_type,
                source_service,
                event_body
            )
            VALUES(
                @Id,
                @Other,
                'transaction.completed',
                'transactions',
                '{}'
            )
            """,
        "reporting" => """
            INSERT INTO reported_positions(
                account_id,
                currency,
                balance,
                reserved,
                version
            )
            VALUES(
                @Id,
                'ZAR',
                123.45,
                3,
                9
            )
            """,
        "scheduling" => """
            INSERT INTO scheduled_reversals(
                id,
                original_transaction_id,
                kind,
                actor_id,
                due_at,
                reason
            )
            VALUES(
                @Id,
                @Other,
                'Refund',
                @Owner,
                @At,
                'Preserved approved review'
            )
            """,
        "debt" => """
            INSERT INTO reversal_reviews(
                id,
                request_body
            )
            VALUES(
                @Id,
                '{}'
            )
            """,
        _ => throw new ArgumentException("Unknown isolated service.")
    };

    private sealed class NativeConfigurationClient(ServiceDatabase database, string caller) : IServiceClient
    {
        public CountryOperationStore Operations { get; } = new(database);
        public SettingsStore Settings { get; private set; } = null!;
        public Dictionary<string, Func<CancellationToken, Task<CountryChangeEligibility>>> Probes { get; } = [];
        public bool RejectSettingsReads { get; set; }
        public Exception? NextOperationReadFailure { get; set; }

        public static async Task<NativeConfigurationClient> Create(ServiceDatabase database, string caller)
        {
            await database.ApplyServiceSchemaMigrations(
                Schema("configuration", database),
                ServiceMigrations.GetServiceMigrations("configuration", "PostgreSql"));
            var client = new NativeConfigurationClient(database, caller);
            client.Settings = new SettingsStore(database, client);
            await client.Settings.InitializeServiceData(default);
            return client;
        }

        public async Task<T> SendServiceRequest<T>(
            string service,
            HttpMethod method,
            string path,
            object? body = null,
            System.Security.Claims.ClaimsPrincipal? actor = null,
            CancellationToken ct = default)
        {
            object result;
            if (service == "configuration" && path == "/settings")
            {
                if (RejectSettingsReads)
                {
                    throw new InvalidOperationException("A posted replay must not reinterpret the current policy.");
                }

                result = await Settings.GetPlatformSettings(ct);
            }
            else if (service == "configuration" && path == "/country-operations")
            {
                result = await Operations.Begin(Assert.IsType<CountryOperationRequest>(body), caller, ct);
            }
            else if (service == "configuration" && path.StartsWith("/country-operations/operations/", StringComparison.Ordinal))
            {
                if (NextOperationReadFailure is { } interrupted)
                {
                    NextOperationReadFailure = null;
                    throw interrupted;
                }

                result = await Operations.GetCountryOperation(Guid.Parse(path.Split('/')[3]), ct);
            }
            else if (service == "configuration" && path.Contains("/settle", StringComparison.Ordinal))
            {
                var uri = new Uri("https://verification.invalid" + path);
                var parts = uri.AbsolutePath.Split('/');
                var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
                var customer = Guid.TryParse(query["customerId"], out var id) ? id : (Guid?)null;
                await Operations.Settle(parts[2], Guid.Parse(parts[3]), caller, ct, customer);
                result = new object();
            }
            else if (path.EndsWith("/country-eligibility", StringComparison.Ordinal)
                || path.EndsWith("/eligibility", StringComparison.Ordinal))
            {
                result = Probes.TryGetValue(service, out var probe) ? await probe(ct) : new CountryChangeEligibility();
            }
            else
            {
                throw new InvalidOperationException("Unexpected native service dependency: " + service + path);
            }

            return (T)result;
        }
    }

    private sealed class TestFinancialConfiguration(SettingsClient client) : IFinancialConfiguration
    {
        public Task<PlatformSettings> GetFinancialConfiguration(CancellationToken ct) => Task.FromResult(client.Current);

        public Task<CountryOperation> RequireOperation(Guid operationId, CancellationToken ct) =>
            client.SendServiceRequest<CountryOperation>(
            "configuration",
            HttpMethod.Get,
            "/country-operations/operations/" + operationId,
            ct: ct);
    }

    // A typed test double for unrelated store roundtrips. Native fence/race tests below use the real registry.
    private sealed class SettingsClient : IServiceClient
    {
        private readonly Dictionary<(string Kind, Guid Request), CountryOperation> operations = [];
        private readonly object gate = new();
        public PlatformSettings Current { get; set; } = Settings;

        private readonly Guid owner = Guid.NewGuid();
        private readonly Guid account = Guid.NewGuid();

        public Task<T> SendServiceRequest<T>(
            string service,
            HttpMethod method,
            string path,
            object? body = null,
            System.Security.Claims.ClaimsPrincipal? actor = null,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            object result;
            lock (gate)
            {
                if (service == "configuration" && path == "/settings")
                {
                    result = Current;
                }
                else if (path.EndsWith("/country-eligibility", StringComparison.Ordinal)
                    || path.EndsWith("/eligibility", StringComparison.Ordinal))
                {
                    result = new CountryChangeEligibility();
                }
                else if (service == "configuration" && path == "/country-operations")
                {
                    var request = Assert.IsType<CountryOperationRequest>(body);
                    var key = (request.SourceKind, request.RequestId);
                    if (operations.TryGetValue(key, out var prior))
                    {
                        if (prior.CustomerId != request.CustomerId
                            || prior.Currency != request.Currency
                            || prior.CountryCode != request.CountryCode
                            || prior.OriginalTransactionId != request.OriginalTransactionId
                            || request.OperationId is { } id
                            && id != prior.OperationId)
                        {
                            throw new PlatformException(409, "request_conflict", "The captured test operation differs.");
                        }

                        result = prior;
                    }
                    else
                    {
                        if (request.RequestId == Guid.Empty || request.SourceKind is "Account" or "Payment" && request.CustomerId is null)
                        {
                            throw new PlatformException(400, "request_required", "The test claim requires an owner and request.");
                        }

                        var historical = request.SourceKind == "Import" || request.OriginalTransactionId is not null;
                        if (request.ExpectedVersion is { } version
                            && version != Current.Version
                            || !historical
                            && !CountryCurrencyPolicy.IsCurrent(Current, request.CountryCode, request.Currency))
                        {
                            throw new PlatformException(409, "settings_changed", "The test operation no longer matches saved policy.");
                        }

                        var operation = new CountryOperation
                        {
                            RequestId = request.RequestId,
                            OperationId = request.OperationId ?? Guid.NewGuid(),
                            SourceKind = request.SourceKind,
                            CustomerId = request.CustomerId,
                            PolicyVersion = Current.Version,
                            CountryCode = request.CountryCode,
                            Currency = request.Currency,
                            OriginalTransactionId = request.OriginalTransactionId
                        };
                        operations.Add(key, operation);
                        result = operation;
                    }
                }
                else if (service == "configuration" && path.StartsWith("/country-operations/operations/", StringComparison.Ordinal))
                {
                    var id = Guid.Parse(path.Split('/')[3]);
                    result = operations.Values.Single(operation => operation.OperationId == id);
                }
                else if (service == "configuration" && path.Contains("/settle", StringComparison.Ordinal))
                {
                    var uri = new Uri("https://verification.invalid" + path);
                    var parts = uri.AbsolutePath.Split('/');
                    var key = (parts[2], Guid.Parse(parts[3]));
                    if (operations.TryGetValue(key, out var operation))
                    {
                        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
                        var customer = Guid.TryParse(query["customerId"], out var customerId) ? customerId : (Guid?)null;
                        if (operation.SourceKind is "Account" or "Payment" && customer != operation.CustomerId)
                        {
                            throw new PlatformException(403, "owner_required", "The captured test operation belongs to another owner.");
                        }

                        operation.SettledAt ??= DateTimeOffset.UtcNow;
                    }

                    result = new object();
                }
                else if (service == "transactions" && path.StartsWith("/transactions/", StringComparison.Ordinal))
                {
                    var id = Guid.Parse(path.Split('/')[2]);
                    result = new TransactionRecord
                    {
                        Id = id,
                        PaymentId = id,
                        UserId = owner,
                        Kind = "CardPurchase",
                        Reference = "NATIVE-ORIGINAL-DEBIT",
                        AccountId = account,
                        RecipientAccountId = null,
                        Amount = 10,
                        Currency = "ZAR",
                        TargetAmount = null,
                        TargetCurrency = null,
                        Fee = 0,
                        Status = "Completed",
                        FailureCode = null,
                        CreatedAt = DateTimeOffset.UtcNow,
                        UpdatedAt = DateTimeOffset.UtcNow
                    };
                }
                else if (service == "accounts" && path.StartsWith("/accounts/", StringComparison.Ordinal))
                {
                    result = new AccountRecord
                    {
                        Id = account,
                        UserId = owner,
                        Number = "123456789012",
                        Type = "Cheque",
                        CountryCode = "ZA",
                        Currency = "ZAR",
                        Status = "Active",
                        LedgerBalance = 100,
                        ReservedBalance = 0,
                        Version = 0,
                        CreatedAt = DateTimeOffset.UtcNow
                    };
                }
                else
                {
                    throw new InvalidOperationException("Unexpected isolated store dependency: " + service + path);
                }
            }

            return Task.FromResult((T)result);
        }
    }

    private class DatabaseFixture(NpgsqlConnection admin, string name, ServiceDatabase database) : IAsyncDisposable
    {
        public ServiceDatabase Database { get; } = database;

        public static async Task<DatabaseFixture> Create(string service)
        {
            PlatformMapping.InitializeDapperMappings();
            var master = Environment.GetEnvironmentVariable("PORTAL_TEST_POSTGRESQL") ?? throw new InvalidOperationException("A native disposable test connection is required.");
            var admin = new NpgsqlConnection(master);
            await admin.OpenAsync();
            var name = "portal_service_verify_" + Guid.NewGuid().ToString("N");
            await admin.ExecuteAsync($"CREATE DATABASE \"{name}\"");
            var cs = new NpgsqlConnectionStringBuilder(master)
            {
                Database = name,
                Pooling = false
            }.ConnectionString;
            return new(
                admin,
                name,
                new ServiceDatabase(TestMigrationConfiguration.ApplyServiceMigrationSettings(new ServiceOptions
                {
                    Name = service,
                    DatabaseProvider = "PostgreSql",
                    ConnectionString = cs
                })));
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (!Regex.IsMatch(name, "^portal_service_verify_[0-9a-f]{32}$"))
                {
                    throw new InvalidOperationException("Refusing to drop a database outside this disposable fixture.");
                }

                await admin.ExecuteAsync($"DROP DATABASE \"{name}\" WITH (FORCE)");
            }
            finally
            {
                await admin.DisposeAsync();
            }
        }
    }
}
