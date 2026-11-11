using System.Text.Json;
using Dapper;
using DisputePortal.Api.Repository.Data;
using DisputePortal.Platform.Contracts;
using DisputePortal.Platform.Infrastructure;
using DisputePortal.Platform.Transactions;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace DisputePortal.IntegrationTests;
// Financial services use PostgreSQL. This fixture owns a separate database;
// it never uses the API's SQL Server book or the running service databases.
[Trait("Provider", "PostgreSql")]
public class TransactionActivityIdentityTests : IAsyncLifetime
{
    private readonly string databaseName = "DisputePortal_Test_Activity_" + Guid.NewGuid().ToString("N");
    private string? adminConnection;
    private string connectionString = "";
    private PostgreSqlContainer? container;
    private ServiceDatabase database = null!;

    public async Task InitializeAsync()
    {
        PlatformMapping.InitializeDapperMappings();
        adminConnection = Environment.GetEnvironmentVariable("PORTAL_TEST_POSTGRESQL");
        if (!string.IsNullOrWhiteSpace(adminConnection))
        {
            connectionString = new NpgsqlConnectionStringBuilder(adminConnection)
            {
                Database = databaseName
            }.ConnectionString;
            await new DatabaseConnectionFactory(DatabaseProvider.PostgreSql, connectionString).EnsureDatabaseExists(default);
        }
        else
        {
            container = new PostgreSqlBuilder("postgres:16-alpine").WithDatabase(databaseName).WithUsername("portal").WithPassword("test-only-password").Build();
            await container.StartAsync();
            connectionString = container.GetConnectionString();
        }

        database = new ServiceDatabase(DisputePortal.TestFixtures.TestRuntimeConfiguration.ApplyServiceMigrationSettings(new ServiceOptions
        {
            Name = "transactions",
            DatabaseProvider = "PostgreSql",
            ConnectionString = connectionString
        }));
        await database.ApplyServiceSchemaMigrations(
            TransactionStore.Schema + HistoryStore.Schema + ReceivedActivityStore.Schema,
            ServiceMigrations.GetServiceMigrations("transactions", "PostgreSql"));
    }

    public async Task DisposeAsync()
    {
        if (container is not null)
        {
            await container.DisposeAsync();
            return;
        }

        if (adminConnection is null
            || !System.Text.RegularExpressions.Regex.IsMatch(databaseName, "^DisputePortal_Test_Activity_[0-9a-f]{32}$"))
        {
            return;
        }

        NpgsqlConnection.ClearAllPools();
        await using var connection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(adminConnection) { Database = "postgres" }.ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync($"""DROP DATABASE IF EXISTS "{databaseName}" WITH (FORCE)""");
    }

    [Fact]
    public async Task Activity_list_returns_one_record_per_public_identity_with_the_same_source_precedence_as_details()
    {
        var owner = Guid.NewGuid();
        var otherOwner = Guid.NewGuid();
        var source = Guid.NewGuid();
        var target = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var payment = new PaymentEvent
        {
            PaymentId = paymentId,
            UserId = owner,
            RequestId = Guid.NewGuid(),
            Kind = "Transfer",
            AccountId = source,
            RecipientAccountId = target,
            Amount = 10,
            State = "Authorized",
            Reference = "ORIGINAL-TRANSFER",
            Currency = "ZAR"
        };
        var incomingId = Guid.NewGuid();
        var refundId = Guid.NewGuid();
        var purchaseId = Guid.NewGuid();
        var secondIncomingId = Guid.NewGuid();
        TransactionRecord Record(Guid id, Guid account, string kind, string reference, string direction, string? description = null) => new global::DisputePortal.Platform.Contracts.TransactionRecord
        {
            Id = id,
            PaymentId = paymentId,
            UserId = owner,
            Kind = kind,
            Reference = reference,
            AccountId = account,
            RecipientAccountId = null,
            Amount = 10,
            Currency = "ZAR",
            TargetAmount = null,
            TargetCurrency = null,
            Fee = 0,
            Status = "Completed",
            FailureCode = null,
            CreatedAt = now,
            UpdatedAt = now,
            Direction = direction,
            Description = description
        };
        await using var connection = await database.OpenServiceDatabaseConnection();
        await connection.ExecuteAsync(
            """
            INSERT INTO transactions."transactions"("publicId",
                "paymentPublicId",
                "customerPublicId",
                "paymentBody",
                status,
                "createdAt",
                "updatedAt")
            VALUES(@Id,
                @Id,
                @Owner,
                CAST(@Body AS jsonb),
                'Completed',
                @At,
                @At)
            """,
            new
            {
                Id = paymentId,
                Owner = owner,
                Body = JsonSerializer.Serialize(payment, DomainEvent.Json),
                At = now.AddHours(-1)
            });
        var history = new[]
        {
            new global::DisputePortal.Platform.Contracts.TransactionRecord(Record(
                paymentId,
                source,
                "Transfer",
                "STALE-IMPORTED-COPY",
                "Debit"))
            {
                Status = "Pending"
            },
            Record(incomingId, target, "Transfer", "APPROVED-INCOMING", "Credit", "Approved incoming history"),
            Record(refundId, source, "Refund", "RF-LINKED-CREDIT", "Credit"),
            Record(purchaseId, source, "CardPurchase", "SEPARATE-PURCHASE", "Debit"),
            new global::DisputePortal.Platform.Contracts.TransactionRecord(Record(
                Guid.NewGuid(),
                source,
                "CardPurchase",
                "OTHER-CUSTOMER",
                "Debit"))
            {
                UserId = otherOwner
            }
        };
        foreach (var record in history)
            await connection.ExecuteAsync(
            """
                INSERT INTO transactions."priorTransactions"("publicId",
                    "customerPublicId",
                    "accountPublicId",
                    "sourceHash",
                    body,
                    "occurredAt")
                VALUES(@Id,
                    @UserId,
                    @AccountId,
                    @Hash,
                    CAST(@Body AS jsonb),
                    @At)
                """,
            new
            {
                record.Id,
                record.UserId,
                record.AccountId,
                Hash = new string('a', 64),
                Body = JsonSerializer.Serialize(record, DomainEvent.Json),
                At = record.CreatedAt
            });
        foreach (var record in new[]
        {
            Record(incomingId, target, "Transfer", "RECEIVED-COPY", "Credit"),
            Record(secondIncomingId, target, "Transfer", "SEPARATE-INCOMING", "Credit")
        }

        )
        {
            var sourcePayment = new global::DisputePortal.Platform.Contracts.PaymentEvent(payment)
            {
                PaymentId = Guid.NewGuid(),
                UserId = Guid.NewGuid()
            };
            await connection.ExecuteAsync(
                """
                INSERT INTO transactions.transactions("publicId", "paymentPublicId", "customerPublicId", "paymentBody", status)
                VALUES(@PaymentId, @PaymentId, @UserId, CAST(@Body AS jsonb), 'Completed')
                """,
                new
                {
                    sourcePayment.PaymentId,
                    sourcePayment.UserId,
                    Body = JsonSerializer.Serialize(sourcePayment, DomainEvent.Json)
                });
            await connection.ExecuteAsync(
                """
                INSERT INTO transactions."receivedTransactions"("publicId",
                    "transactionId",
                    "accountPublicId",
                    "customerPublicId",
                    body,
                    "occurredAt")
                VALUES(@Id,
                    (SELECT id FROM transactions.transactions WHERE "publicId"=@Original),
                    @AccountId,
                    @UserId,
                    CAST(@Body AS jsonb),
                    @At)
                """,
                new
                {
                    record.Id,
                    Original = sourcePayment.PaymentId,
                    record.AccountId,
                    record.UserId,
                    Body = JsonSerializer.Serialize(record, DomainEvent.Json),
                    At = record.CreatedAt
                });
        }

        const string rowCountSql = "SELECT (SELECT COUNT(*) FROM transactions.\"transactions\")+(SELECT COUNT(*) FROM transactions.\"priorTransactions\")+(SELECT COUNT(*) FROM transactions.\"receivedTransactions\")";
        var rowCount = await connection.ExecuteScalarAsync<int>(rowCountSql);
        var store = new TransactionStore(database, new HistoryStore(database), new ReceivedActivityStore(database));
        var rows = Assert.IsType<TransactionRecord[]>(await store.GetCustomerTransactions(owner, default));
        Assert.Equal(5, rows.Length);
        Assert.Equal(rows.Length, rows.Select(record => record.Id).Distinct().Count());
        var original = Assert.Single(rows, record => record.Id == paymentId);
        Assert.Equal("ORIGINAL-TRANSFER", original.Reference);
        Assert.Equal("Completed", original.Status);
        Assert.Equivalent(await store.GetTransactionById(paymentId, owner, default), original, strict: true);
        Assert.Equal("APPROVED-INCOMING", Assert.Single(rows, record => record.Id == incomingId).Reference);
        Assert.Equivalent(
            await store.GetTransactionById(incomingId, owner, default),
            Assert.Single(rows, record => record.Id == incomingId),
            strict: true);
        Assert.Equal("Credit", Assert.Single(rows, record => record.Id == refundId).Direction);
        Assert.Contains(rows, record => record.Id == purchaseId);
        Assert.Contains(rows, record => record.Id == secondIncomingId);
        Assert.All(rows, record => Assert.Equal(owner, record.UserId));
        Assert.Single(Assert.IsType<TransactionRecord[]>(await store.GetCustomerTransactions(otherOwner, default)));
        Assert.Equal(rowCount, await connection.ExecuteScalarAsync<int>(rowCountSql));
    }
}
