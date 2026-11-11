using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using DisputePortal.Api.Models.Domain;
using DisputePortal.Api.Models.Exceptions;
using DisputePortal.Api.Repository;
using DisputePortal.Api.Repository.Data;
using DisputePortal.Api.Service.Disputes;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;
using DisputePortal.Api.Interfaces.Data;
using DisputePortal.Api.Interfaces.Security;

namespace DisputePortal.UnitTests;

public sealed class DisputeRollbackFailureTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Cleanup_preserves_the_original_failure_after_request_cancellation(int cleanupMode)
    {
        using var request = new CancellationTokenSource();
        var original = new TestDatabaseFailure("Confidential original database details.");
        Exception? cleanup = cleanupMode switch
        {
            1 => new TestDatabaseFailure("Confidential rollback database details."),
            2 => new InvalidOperationException("Confidential completed transaction details."),
            _ => null
        };
        var connection = new FailureConnection(original, cleanup, false, request);
        var logger = new CaptureLogger();
        var repository = Repository(connection, original, logger);
        var error = await Assert.ThrowsAsync<TestDatabaseFailure>(() => Create(repository, request.Token));
        Assert.Same(original, error);
        Assert.True(request.IsCancellationRequested);
        Assert.Equal(1, connection.Transaction.RollbackCalls);
        Assert.False(connection.Transaction.RollbackToken.CanBeCanceled);
        Assert.Equal(cleanupMode == 0 ? 0 : 1, logger.Messages.Count);
        Assert.All(
            logger.Messages,
            entry =>
        {
            Assert.Null(entry.Error);
            Assert.DoesNotContain("Confidential", entry.Text);
        });
    }

    [Fact]
    public async Task A_provider_completed_transaction_does_not_attempt_another_rollback()
    {
        using var request = new CancellationTokenSource();
        var original = new TestDatabaseFailure("Original timeout or uncertain commit failure.");
        var connection = new FailureConnection(original, new InvalidOperationException("Transaction has completed."), true, request);
        var error = await Assert.ThrowsAsync<TestDatabaseFailure>(() => Create(
            Repository(connection, original, new CaptureLogger()),
            request.Token));
        Assert.Same(original, error);
        Assert.Equal(0, connection.Transaction.RollbackCalls);
    }

    [Fact]
    public async Task A_unique_constraint_failure_still_becomes_a_conflict_when_cleanup_fails()
    {
        using var request = new CancellationTokenSource();
        var original = new TestDatabaseFailure("Duplicate record.");
        var connection = new FailureConnection(original, new InvalidOperationException("Transaction has completed."), false, request);
        var repository = Repository(connection, original, new CaptureLogger(), unique: true);
        await Assert.ThrowsAsync<PortalConflictException>(() => Create(repository, request.Token));
        Assert.Equal(1, connection.Transaction.RollbackCalls);
        Assert.False(connection.Transaction.RollbackToken.CanBeCanceled);
    }

    private static Task Create(DisputeRepository repository, CancellationToken ct) => repository.CreateDispute(
        Guid.NewGuid(),
        new CreateDisputeCommand
        {
            TransactionId = Guid.NewGuid(),
            Category = "Other",
            Reason = "Other",
            Description = "Please investigate this transaction."
        },
        "stable-request-key",
        ct);

    private static DisputeRepository Repository(
        FailureConnection connection,
        DbException original,
        CaptureLogger logger,
        bool unique = false)
    {
        var factory = Substitute.For<IDatabaseConnectionFactory>();
        factory.OpenConnection(Arg.Any<CancellationToken>()).Returns(Task.FromResult<DbConnection>(connection));
        var dialect = Substitute.For<IDatabaseDialect>();
        dialect.Provider.Returns(DatabaseProvider.SqlServer);
        dialect.OperationsLockSql.Returns(DatabaseDialect.GetDatabaseDialect(DatabaseProvider.SqlServer).OperationsLockSql);
        dialect.IsUniqueConstraintViolation(original).Returns(unique);
        var policy = new CaseServicePolicy();
        return new(
            factory,
            dialect,
            TimeZoneInfo.FindSystemTimeZoneById("Africa/Johannesburg"),
            policy,
            new DisputeOperationsRepository(factory, dialect, policy),
            new DisputeRiskRepository(
                factory,
                dialect,
                TimeZoneInfo.FindSystemTimeZoneById("Africa/Johannesburg"),
                Substitute.For<IOtpHasher>(),DisputePortal.TestFixtures.TestRuntimeConfiguration.Read()),
            Substitute.For<DisputePortal.Api.Interfaces.Repositories.IPortalSettingsRepository>(),
            logger);
    }

    private sealed class TestDatabaseFailure(string message) : DbException(message);
    private sealed class FailureConnection : DbConnection
    {
        private readonly DbCommand command = Substitute.For<DbCommand>();
        public FailureTransaction Transaction { get; }

        public FailureConnection(DbException original, Exception? cleanup, bool providerCompleted, CancellationTokenSource request)
        {
            Transaction = new(this, cleanup);
            command.ExecuteNonQueryAsync(Arg.Any<CancellationToken>()).Returns(_ =>
            {
                Transaction.Completed = providerCompleted;
                request.Cancel();
                return Task.FromException<int>(original);
            });
        }

        [AllowNull]
        public override string ConnectionString { get; set; } = "";
        public override string Database => "test";
        public override string DataSource => "test";
        public override string ServerVersion => "1";
        public override ConnectionState State => ConnectionState.Open;

        public override void Open()
        {
        }

        public override void Close()
        {
        }

        public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => Transaction;

        protected override DbCommand CreateDbCommand() => command;
    }

    private sealed class FailureTransaction(DbConnection connection, Exception? cleanup) : DbTransaction
    {
        public bool Completed { get; set; }
        public int RollbackCalls { get; private set; }
        public CancellationToken RollbackToken { get; private set; }
        public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;
        protected override DbConnection? DbConnection => Completed ? null : connection;

        public override void Commit() => throw new NotSupportedException();

        public override void Rollback() => throw new NotSupportedException();

        public override Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            RollbackCalls++;
            RollbackToken = cancellationToken;
            return cleanup is null ? Task.CompletedTask : Task.FromException(cleanup);
        }
    }

    private sealed class CaptureLogger : ILogger<DisputeRepository>
    {
        public List<(string Text, Exception? Error)> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? error, Func<TState, Exception?, string> formatter) =>
            Messages.Add((formatter(state, error), error));
    }
}
