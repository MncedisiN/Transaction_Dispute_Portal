using DisputePortal.TestFixtures;
using System.Data.Common;
using System.Security.Claims;
using DisputePortal.Api.Platform;
using DisputePortal.Api.Repository;
using DisputePortal.Api.Repository.Data;
using DisputePortal.Api.Models.Domain;
using DisputePortal.Platform.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Data.SqlClient;
using Npgsql;
using Dapper;
using DisputePortal.Api.Service.Disputes;
using DisputePortal.Platform.Infrastructure;
using Xunit;
using DisputePortal.Api.Interfaces.Repositories;
using DisputePortal.Api.Interfaces.Security;
using DisputePortal.Platform.Infrastructure.Interfaces;

namespace DisputePortal.IntegrationTests;

public sealed partial class DisputeWorkflowRepositoryTests
{
    [Fact]
    public async Task Retained_service_events_project_an_account_and_incoming_transfer_exactly_once_on_either_provider()
    {
        var database = ProjectionDatabase();
        var remote = new ProjectionAccounts(new AccountRecord
        {
            Id = Guid.NewGuid(),
            UserId = Customer,
            Number = "1122334455",
            Type = "Savings",
            CountryCode = "ZA",
            Currency = "ZAR",
            Status = "Active",
            LedgerBalance = 0,
            ReservedBalance = 0,
            Version = 1,
            CreatedAt = DateTimeOffset.UtcNow
        });
        var services = new ServiceCollection();
        services.AddSingleton<IFinanceRepository>(new FinanceRepository(
            _connections,
            DatabaseDialect.GetDatabaseDialect(_provider),
            TimeZoneInfo.FindSystemTimeZoneById("Africa/Johannesburg"),
            TestRuntimeConfiguration.Read(),
            new CaseServicePolicy()));
        services.AddSingleton<ISecretProtector>(new TestProtector());
        await using var provider = services.BuildServiceProvider();
        var consumer = new IdentityProjectionConsumer(database, provider.GetRequiredService<IServiceScopeFactory>(), remote);
        var activated = DomainEvent.CreateDomainEvent(
            "accounts",
            EventTypes.AccountActivated,
            new AccountEvent
            {
                AccountId = remote.Account.Id,
                UserId = Customer,
                CountryCode = "ZA",
                Currency = "ZAR",
                AccountType = "Savings"
            },
            Guid.NewGuid());
        await consumer.Handle(activated, default);
        Assert.Equal(2, remote.Calls);
        Assert.Equal(2, remote.SettingsCalls);
        await consumer.Handle(activated, default);
        Assert.Equal(2, remote.Calls);
        Assert.Equal(2, remote.SettingsCalls);
        var incoming = new TransactionRecord
        {
            Id = Guid.NewGuid(),
            PaymentId = Guid.NewGuid(),
            UserId = Customer,
            Kind = "Transfer",
            Reference = "INTEGRITY-" + Guid.NewGuid().ToString("N"),
            AccountId = remote.Account.Id,
            RecipientAccountId = null,
            Amount = 25m,
            Currency = "ZAR",
            TargetAmount = null,
            TargetCurrency = null,
            Fee = 0,
            Status = "Completed",
            FailureCode = null,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Direction = "Credit"
        };
        var received = DomainEvent.CreateDomainEvent("transactions", "transaction.received", incoming, Guid.NewGuid());
        await consumer.Handle(received, default);
        await consumer.Handle(received, default);
        await using var connection = await _connections.OpenConnection(default);
        Assert.Equal(2, remote.Calls);
        Assert.Equal(2, remote.SettingsCalls);
        Assert.Equal(
            1,
            await connection.ExecuteScalarAsync<int>(
                """
            SELECT COUNT(*)
            FROM "accounts"."accounts" AS "customer_accounts"
            WHERE "customer_accounts"."publicId" = @Id
            """,
                new { remote.Account.Id }));
        Assert.Equal(
            1,
            await connection.ExecuteScalarAsync<int>(
                """
            SELECT COUNT(*)
            FROM "transactions"."transactions" AS "transactions"
            WHERE "transactions"."publicId" = @Id
            AND "transactions"."direction" = 'Credit'
            AND(
                SELECT "related1"."code"
                FROM "configuration"."transactionTypes" AS "related1"
                WHERE "related1"."id" = "transactions"."transactionTypeId") = 'Transfer'
            """,
                new { incoming.Id }));
        Assert.Equal(
            25m,
            await connection.ExecuteScalarAsync<decimal>(
                """
            SELECT SUM("financial_ledger"."currentDelta")
            FROM "ledger"."projectionEntries" AS "financial_ledger"
            WHERE(
                SELECT "related1"."publicId"
                FROM "accounts"."accounts" AS "related1"
                WHERE "related1"."id" = "financial_ledger"."accountId") = @Id
            """,
                new { Id = remote.Account.Id }));
        var hash = await connection.ExecuteScalarAsync<string>(
            """
            SELECT "platform_account_migrations"."sourceHash" AS "source_hash"
            FROM "integration"."accountMigrations" AS "platform_account_migrations"
            WHERE(
                SELECT "related1"."publicId"
                FROM "accounts"."accounts" AS "related1"
                WHERE "related1"."id" = "platform_account_migrations"."accountId") = @Id
            """,
            new { remote.Account.Id });
        Assert.Equal(64, hash!.Length);
        Assert.NotEqual(new string('0', 64), hash);
        Assert.Equal(
            2,
            await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM service_inbox WHERE event_id IN (@Activation,@Receipt)",
                new { Activation = activated.EventId, Receipt = received.EventId }));
    }

    [Fact]
    public async Task Configuration_projection_preserves_newer_country_epochs_and_stale_activation_cannot_reactivate_a_blocked_account()
    {
        var saved = await new CountryPolicyFixtureClient().SendServiceRequest<PlatformSettings>(
            "configuration",
            HttpMethod.Get,
            "/settings");
        var current = new global::DisputePortal.Platform.Contracts.PlatformSettings(saved)
        {
            Version = 5
        };
        var foreign = new global::DisputePortal.Platform.Contracts.PlatformSettings(current)
        {
            OperatingCountry = "ZW",
            Currencies = [..current.Currencies, new CurrencyDefinition
            {
                Code = "ZWG",
                Name = "ZiG",
                Symbol = "ZiG",
                Decimals = 2
            }

            ],
            Countries = [..current.Countries, new CountryDefinition
            {
                Code = "ZW",
                Name = "Zimbabwe",
                Flag = "",
                DialCode = "+263",
                Locale = "en-ZW",
                TimeZone = "Africa/Harare",
                DefaultCurrency = "ZWG",
                Currencies = ["ZWG"],
                Documents = [],
                Regions = [],
                RegionLabel = "Province",
                PostalLabel = "Postal Code",
                PostalRequired = false
            }

            ]
        };
        var remote = new ProjectionAccounts(new AccountRecord
        {
            Id = Guid.NewGuid(),
            UserId = Customer,
            Number = "1122334455",
            Type = "Savings",
            CountryCode = "ZA",
            Currency = "ZAR",
            Status = "Active",
            LedgerBalance = 0,
            ReservedBalance = 0,
            Version = 1,
            CreatedAt = DateTimeOffset.UtcNow,
            CountryPolicyVersion = 5
        })
        {
            SavedSettings = current
        };
        var services = new ServiceCollection();
        services.AddSingleton<IFinanceRepository>(new FinanceRepository(
            _connections,
            DatabaseDialect.GetDatabaseDialect(_provider),
            TimeZoneInfo.FindSystemTimeZoneById("Africa/Johannesburg"),
            TestRuntimeConfiguration.Read(),
            new CaseServicePolicy()));
        services.AddSingleton<ISecretProtector>(new TestProtector());
        await using var provider = services.BuildServiceProvider();
        var consumer = new IdentityProjectionConsumer(ProjectionDatabase(), provider.GetRequiredService<IServiceScopeFactory>(), remote);
        await using var connection = await _connections.OpenConnection(default);
        async Task AssertProjection(string status, int version)
        {
            var actual = await connection.QuerySingleAsync<(string Status, int Version, string Currency)>(
                """
                SELECT "accountStatus" AS Status,
                    "countryPolicyVersion" AS Version,
                    "currencyCode" AS Currency
                FROM "accounts"."accounts"
                WHERE "publicId" = @Id
                """,
                new { remote.Account.Id });
            Assert.Equal((status, version, "ZAR"), actual);
        }

        var activation = DomainEvent.CreateDomainEvent(
            "accounts",
            EventTypes.AccountActivated,
            new AccountEvent
            {
                AccountId = remote.Account.Id,
                UserId = Customer,
                CountryCode = "ZA",
                Currency = "ZAR",
                AccountType = "Savings"
            },
            Guid.NewGuid());
        await consumer.Handle(activation, default);
        await AssertProjection("Active", 5);
        remote.SavedSettings = new global::DisputePortal.Platform.Contracts.PlatformSettings(foreign)
        {
            Version = 4
        };
        await consumer.Handle(
            DomainEvent.CreateDomainEvent("configuration", "configuration.updated", new { Version = 4 }, Guid.NewGuid()),
            default);
        await AssertProjection("Active", 5);
        remote.SavedSettings = new global::DisputePortal.Platform.Contracts.PlatformSettings(foreign)
        {
            Version = 6
        };
        var changed = DomainEvent.CreateDomainEvent("configuration", "configuration.updated", new { Version = 6 }, Guid.NewGuid());
        await consumer.Handle(changed, default);
        var settingsReads = remote.SettingsCalls;
        await consumer.Handle(changed, default);
        Assert.Equal(settingsReads, remote.SettingsCalls);
        await AssertProjection("Blocked", 6);
        remote.SavedSettings = new global::DisputePortal.Platform.Contracts.PlatformSettings(saved)
        {
            Version = 4
        };
        var staleActivation = DomainEvent.CreateDomainEvent(
            "accounts",
            EventTypes.AccountActivated,
            new AccountEvent
            {
                AccountId = remote.Account.Id,
                UserId = Customer,
                CountryCode = "ZA",
                Currency = "ZAR",
                AccountType = "Savings"
            },
            Guid.NewGuid());
        await consumer.Handle(staleActivation, default);
        await AssertProjection("Blocked", 6);
        Assert.Equal(
            1,
            await connection.ExecuteScalarAsync<int>(
                """
            SELECT COUNT(*)
            FROM "accounts"."accounts"
            WHERE "publicId" = @Id
            """,
                new { remote.Account.Id }));
    }

    private ServiceDatabase ProjectionDatabase()
    {
        var options = DisputePortal.TestFixtures.TestRuntimeConfiguration.ApplyServiceMigrationSettings(new ServiceOptions
        {
            Name = "identity",
            DatabaseProvider = _provider.ToString()
        });
        if (_provider == DatabaseProvider.SqlServer)
        {
            options.ConnectionString = new SqlConnectionStringBuilder(_sqlAdmin!)
            {
                InitialCatalog = _databaseName
            }.ConnectionString;
        }
        else if (_pgAdmin is not null)
        {
            options.ConnectionString = new NpgsqlConnectionStringBuilder(_pgAdmin)
            {
                Database = _databaseName
            }.ConnectionString;
        }
        else
        {
            options.ConnectionString = _postgres!.GetConnectionString();
        }

        return new ServiceDatabase(options);
    }

    [Fact]
    public async Task Failed_service_projection_preserves_the_original_database_error_and_can_retry_after_dependency_arrives()
    {
        var database = ProjectionDatabase();
        var missingOwner = Guid.NewGuid();
        var remote = new ProjectionAccounts(new AccountRecord
        {
            Id = Guid.NewGuid(),
            UserId = missingOwner,
            Number = "1122334455",
            Type = "Savings",
            CountryCode = "ZA",
            Currency = "ZAR",
            Status = "Active",
            LedgerBalance = 0,
            ReservedBalance = 0,
            Version = 1,
            CreatedAt = DateTimeOffset.UtcNow
        });
        var services = new ServiceCollection();
        services.AddSingleton<IFinanceRepository>(new FinanceRepository(
            _connections,
            DatabaseDialect.GetDatabaseDialect(_provider),
            TimeZoneInfo.FindSystemTimeZoneById("Africa/Johannesburg"),
            TestRuntimeConfiguration.Read(),
            new CaseServicePolicy()));
        services.AddSingleton<ISecretProtector>(new TestProtector());
        await using var provider = services.BuildServiceProvider();
        var consumer = new IdentityProjectionConsumer(database, provider.GetRequiredService<IServiceScopeFactory>(), remote);
        var value = DomainEvent.CreateDomainEvent(
            "accounts",
            EventTypes.AccountActivated,
            new AccountEvent
            {
                AccountId = remote.Account.Id,
                UserId = missingOwner,
                CountryCode = "ZA",
                Currency = "ZAR",
                AccountType = "Savings"
            },
            Guid.NewGuid());
        var error = await Assert.ThrowsAnyAsync<DbException>(() => consumer.Handle(value, default));
        // The missing public owner resolves to NULL for the required numeric FK.
        // Preserve that native error; do not replace it with an unrelated rollback failure.
        if (_provider == DatabaseProvider.SqlServer)
        {
            var original = Assert.IsType<SqlException>(error);
            Assert.Equal(515, original.Number);
            Assert.Contains("userId", original.Message);
        }
        else
        {
            var original = Assert.IsType<PostgresException>(error);
            Assert.Equal(PostgresErrorCodes.NotNullViolation, original.SqlState);
            Assert.Equal("accounts", original.SchemaName);
            Assert.Equal("accounts", original.TableName);
            Assert.Equal("userId", original.ColumnName);
        }

        await using var connection = await _connections.OpenConnection(default);
        Assert.Equal(
            0,
            await connection.ExecuteScalarAsync<int>(
                """
            SELECT COUNT(*)
            FROM "accounts"."accounts" AS "customer_accounts"
            WHERE "customer_accounts"."publicId" = @Id
            """,
                new { remote.Account.Id }));
        Assert.Equal(
            0,
            await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM service_inbox WHERE event_id=@Id", new { Id = value.EventId }));
        await connection.ExecuteAsync(
            """
            INSERT INTO "identity"."users"("publicId",
                "loginIdentifier",
                "normalizedLoginIdentifier",
                "passwordHash")
            VALUES(@Id,
                @Login,
                @Normalized,
                'not-used')
            """,
            new
            {
                Id = missingOwner,
                Login = missingOwner.ToString("N") + "@invalid.example",
                Normalized = missingOwner.ToString("N").ToUpperInvariant() + "@INVALID.EXAMPLE"
            });
        await consumer.Handle(value, default);
        await consumer.Handle(value, default);
        Assert.Equal(
            1,
            await connection.ExecuteScalarAsync<int>(
                """
            SELECT COUNT(*)
            FROM "accounts"."accounts" AS "customer_accounts"
            WHERE "customer_accounts"."publicId" = @Id
            """,
                new { remote.Account.Id }));
        Assert.Equal(
            1,
            await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM service_inbox WHERE event_id=@Id", new { Id = value.EventId }));
    }

    [Fact]
    public async Task Native_operation_lock_serializes_connections_and_releases_on_commit_and_rollback()
    {
        var dialect = RelationalDialect.GetDatabaseDialect(_provider.ToString());
        await using var first = await _connections.OpenConnection(default);
        await using var second = await _connections.OpenConnection(default);
        await using var firstTransaction = await first.BeginTransactionAsync();
        await using var secondTransaction = await second.BeginTransactionAsync();
        await RelationalDialect.LockOperations(first, firstTransaction, dialect.OperationsLockSql, default);
        var waiting = RelationalDialect.LockOperations(second, secondTransaction, dialect.OperationsLockSql, default);
        await Task.Delay(150);
        Assert.False(waiting.IsCompleted);
        await firstTransaction.CommitAsync();
        await waiting.WaitAsync(TimeSpan.FromSeconds(10));
        await secondTransaction.RollbackAsync();
        await using var finalTransaction = await first.BeginTransactionAsync();
        await RelationalDialect.LockOperations(first, finalTransaction, dialect.OperationsLockSql, default);
        await finalTransaction.RollbackAsync();
    }

    [Fact]
    public async Task Waiting_for_the_native_operation_lock_honors_request_cancellation_and_rolls_back()
    {
        var dialect = RelationalDialect.GetDatabaseDialect(_provider.ToString());
        await using var holder = await _connections.OpenConnection(default);
        await using var waiter = await _connections.OpenConnection(default);
        await using var held = await holder.BeginTransactionAsync();
        await using var waitingTransaction = await waiter.BeginTransactionAsync();
        await RelationalDialect.LockOperations(holder, held, dialect.OperationsLockSql, default);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RelationalDialect.LockOperations(
            waiter,
            waitingTransaction,
            dialect.OperationsLockSql,
            cancellation.Token));
        await waitingTransaction.RollbackAsync();
        await held.RollbackAsync();
        await using var subsequent = await waiter.BeginTransactionAsync();
        await RelationalDialect.LockOperations(waiter, subsequent, dialect.OperationsLockSql, default);
        await subsequent.RollbackAsync();
    }

    [Fact]
    public async Task Canonical_financial_projection_requires_the_provider_scope_and_clears_it_afterwards()
    {
        var dialect = RelationalDialect.GetDatabaseDialect(_provider.ToString());
        await using var connection = await _connections.OpenConnection(default);
        await connection.ExecuteAsync(
            """
            UPDATE transactions.transactions SET "transactionStatus"='Pending' WHERE "publicId"=@Transaction
            """,
            new { Transaction });
        await connection.ExecuteAsync(
            """
            INSERT INTO "integration"."accountMigrations"("accountId",
                "snapshotId",
                "sourceHash",
                "countryCode",
                "state",
                "capturedAt",
                "activatedAt")
            VALUES((
                    SELECT "resolved1"."id"
                    FROM "accounts"."accounts" AS "resolved1"
                    WHERE "resolved1"."publicId" = @Account),
                @Snapshot,
                @Hash,
                'ZA',
                'Active',
                @Now,
                @Now)
            """,
            new
            {
                Account,
                Snapshot = Guid.NewGuid(),
                Hash = new string('a', 64),
                Now = DateTimeOffset.UtcNow
            });
        const string update = """
            UPDATE "transactions"."transactions"
            SET "transactionStatus" = @Status
            WHERE "transactions"."publicId" = @Transaction
            """;
        var denied = await Assert.ThrowsAnyAsync<DbException>(() => connection.ExecuteAsync(update, new { Status = "Posted", Transaction }));
        Assert.Contains("Canonical financial records", denied.Message);
        await using (var transaction = await connection.BeginTransactionAsync())
        {
            await connection.ExecuteAsync(new CommandDefinition(dialect.OperationsLockSql, transaction: transaction));
            await connection.ExecuteAsync(new CommandDefinition(dialect.EnableFinancialProjectionSql, transaction: transaction));
            try
            {
                await connection.ExecuteAsync(update, new { Status = "Posted", Transaction }, transaction);
            }
            finally
            {
                await connection.ExecuteAsync(new CommandDefinition(dialect.DisableFinancialProjectionSql, transaction: transaction));
            }

            await transaction.CommitAsync();
        }

        Assert.Equal(
            100m,
            await connection.ExecuteScalarAsync<decimal>(
                """
            SELECT "transactions"."amount" AS "amount"
            FROM "transactions"."transactions" AS "transactions"
            WHERE "transactions"."publicId" = @Transaction
            """,
                new { Transaction }));
        var subsequent = await Assert.ThrowsAnyAsync<DbException>(() => connection.ExecuteAsync(
            update,
            new { Status = "Reversed", Transaction }));
        Assert.Contains("Canonical financial records", subsequent.Message);
        Assert.Equal(
            100m,
            await connection.ExecuteScalarAsync<decimal>(
                """
            SELECT "transactions"."amount" AS "amount"
            FROM "transactions"."transactions" AS "transactions"
            WHERE "transactions"."publicId" = @Transaction
            """,
                new { Transaction }));
        await using (var immutableScope = await connection.BeginTransactionAsync())
        {
            await connection.ExecuteAsync(new CommandDefinition(dialect.EnableFinancialProjectionSql, transaction: immutableScope));
            try
            {
                var mutation = await Assert.ThrowsAnyAsync<DbException>(() => connection.ExecuteAsync(
                    """
                    UPDATE transactions.transactions SET amount=amount+1 WHERE "publicId"=@Transaction
                    """,
                    new { Transaction },
                    immutableScope));
                Assert.Contains("Original bank transaction values are immutable", mutation.Message);
            }
            finally
            {
                await immutableScope.RollbackAsync();
                await connection.ExecuteAsync(dialect.DisableFinancialProjectionSql);
            }
        }

        Assert.Equal(
            100m,
            await connection.ExecuteScalarAsync<decimal>(
                """
            SELECT amount FROM transactions.transactions WHERE "publicId"=@Transaction
            """,
                new { Transaction }));
    }

    [Fact]
    public async Task Pending_ledger_outcome_blocks_unrelated_case_status_changes_on_both_providers()
    {
        var created = await _repository.CreateDispute(
            Customer,
            new CreateDisputeCommand
            {
                TransactionId = Transaction,
                Category = "Other",
                Reason = "Other",
                Description = "Review the financial payment records."
            },
            "pending-finance-guard",
            default);
        await using var connection = await _connections.OpenConnection(default);
        await connection.ExecuteAsync(
            """
            INSERT INTO "disputes"."financialCommands"("publicId",
                "requestId",
                "disputeId",
                "originalTransactionId",
                "accountId",
                "customerId",
                "kind",
                "amount",
                "currencyCode",
                "reason",
                "expectedVersion",
                "correlation",
                "state")
            VALUES(@Id,
                @Request,
                (
                    SELECT "resolved1"."id"
                    FROM "disputes"."disputes" AS "resolved1"
                    WHERE "resolved1"."publicId" = @Dispute),
                (
                    SELECT "resolved2"."id"
                    FROM "transactions"."transactions" AS "resolved2"
                    WHERE "resolved2"."publicId" = @Transaction),
                (
                    SELECT "resolved3"."id"
                    FROM "accounts"."accounts" AS "resolved3"
                    WHERE "resolved3"."publicId" = @Account),
                (
                    SELECT "resolved4"."id"
                    FROM "identity"."users" AS "resolved4"
                    WHERE "resolved4"."publicId" = @Customer),
                'Refund',
                100,
                'ZAR',
                'Await confirmed ledger outcome',
                1,
                @Correlation,
                'Queued')
            """,
            new
            {
                Id = Guid.NewGuid(),
                Request = Guid.NewGuid(),
                Dispute = created.Id,
                Transaction,
                Account,
                Customer,
                Correlation = Guid.NewGuid()
            });
        var error = await Assert.ThrowsAnyAsync<DbException>(() => connection.ExecuteAsync(
            """
            UPDATE "disputes"."disputes"
            SET "status" = 'UnderReview'
            WHERE "disputes"."publicId" = @Id
            """,
            new { created.Id }));
        Assert.Contains("A financial request is processing", error.Message);
        Assert.Equal(
            "Submitted",
            await connection.ExecuteScalarAsync<string>(
                """
            SELECT "transaction_disputes"."status" AS "status"
            FROM "disputes"."disputes" AS "transaction_disputes"
            WHERE "transaction_disputes"."publicId" = @Id
            """,
                new { created.Id }));
    }

    private sealed class ProjectionAccounts(AccountRecord account) : IServiceClient
    {
        public AccountRecord Account { get; } = account;
        public int Calls { get; private set; }
        public int SettingsCalls { get; private set; }
        public PlatformSettings? SavedSettings { get; set; }

        public Task<T> SendServiceRequest<T>(
            string service,
            HttpMethod method,
            string path,
            object? body = null,
            ClaimsPrincipal? actor = null,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (service == "configuration" && method == HttpMethod.Get && path == "/settings")
            {
                SettingsCalls++;
                return SavedSettings is { } saved ? Task.FromResult((T)(object)saved) : new CountryPolicyFixtureClient().SendServiceRequest<T>(
                    service,
                    method,
                    path,
                    body,
                    actor,
                    ct);
            }

            Assert.Equal("accounts", service);
            Assert.Equal(HttpMethod.Get, method);
            Assert.Equal("/internal/accounts/" + Account.Id, path);
            Calls++;
            return Task.FromResult((T)(object)Account);
        }
    }
}
