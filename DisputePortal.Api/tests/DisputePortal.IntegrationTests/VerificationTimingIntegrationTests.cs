using Dapper;
using DisputePortal.Api.Configuration;
using DisputePortal.Api.Models.Exceptions;
using DisputePortal.Api.Repository;
using DisputePortal.Api.Repository.Data;
using DisputePortal.Api.Service.Security;
using DisputePortal.TestFixtures;
using Xunit;

namespace DisputePortal.IntegrationTests;

public sealed partial class DisputeWorkflowRepositoryTests
{
    [Fact]
    public async Task Configured_verification_expiry_and_resend_delay_are_persisted_and_enforced()
    {
        var runtime = TestRuntimeConfiguration.Read();
        runtime.VerificationCodeLifetimeMinutes = 3;
        runtime.VerificationResendDelaySeconds = 2;
        var repository = new ContactVerificationRepository(
            _connections,
            DatabaseDialect.GetDatabaseDialect(_provider),
            new TestProtector(),
            new OtpHasher(new InfobipOptions { OtpHashKey = new string('t', 64) }),
            runtime);
        var ct = CancellationToken.None;
        var challenge = await repository.ReserveContactChallenge(Customer, "phone", "+27821110001", "sms", "123456", ct);
        await using var connection = await _connections.OpenConnection(ct);
        var lifetime = await connection.QuerySingleAsync<VerificationTimingRow>(new CommandDefinition(
            """
            SELECT
                "createdAt" AS CreatedAt,
                "expiresAt" AS ExpiresAt
            FROM "identity"."contactVerificationChallenges"
            WHERE "publicId" = @Id
            """,
            new { challenge.Id },
            cancellationToken: ct));
        Assert.Equal(TimeSpan.FromMinutes(3), lifetime.ExpiresAt - lifetime.CreatedAt);
        // PostgreSQL timestamps retain microseconds; SQL Server retains finer ticks.
        Assert.InRange((challenge.ExpiresAt - lifetime.ExpiresAt).Duration(), TimeSpan.Zero, TimeSpan.FromMicroseconds(1));
        await Assert.ThrowsAsync<PortalConflictException>(() => repository.ReserveContactChallenge(
            Customer, "phone", "+27821110002", "sms", "234567", ct));

        // Advance only this isolated challenge's resend age; no wall-clock sleep.
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE "identity"."contactVerificationChallenges"
            SET "createdAt" = @Earlier
            WHERE "publicId" = @Id
            """,
            new { challenge.Id, Earlier = DateTimeOffset.UtcNow.AddSeconds(-3) },
            cancellationToken: ct));
        var next = await repository.ReserveContactChallenge(Customer, "phone", "+27821110002", "sms", "234567", ct);
        Assert.NotEqual(challenge.Id, next.Id);
        Assert.Equal("Failed", await connection.QuerySingleAsync<string>(new CommandDefinition(
            """
            SELECT "state"
            FROM "identity"."contactVerificationChallenges"
            WHERE "publicId" = @Id
            """,
            new { challenge.Id },
            cancellationToken: ct)));
    }

    private class VerificationTimingRow
    {
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset ExpiresAt { get; set; }
    }
}
