using Dapper;
using System.Data.Common;
using Xunit;

namespace DisputePortal.IntegrationTests;

public sealed partial class DisputeWorkflowRepositoryTests
{
    [Fact]
    public async Task Login_flow_resolves_its_challenge_foreign_key_when_internal_primary_keys_differ()
    {
        // Advance only the challenge sequence: do not rely on coincidentally equal primary keys.
        await _repository.CreateIdentityChallenge(OtherCustomer, "Login", "email", "other@test.local", "", "123456", default);
        var login = LoginSecurity();
        var flow = await LoginFlow(login, Customer);
        await using var connection = await _connections.OpenConnection(default);
        var keys = await connection.QuerySingleAsync<(long FlowId, long ChallengeId, long ChallengeForeignKey)>(
            """
            SELECT f.id AS FlowId,
            "i"."id" AS ChallengeId,
            f."challengeId" AS ChallengeForeignKey
            FROM "identity"."loginVerificationFlows" AS f
            JOIN "identity"."identityChallenges" AS i
            ON "i"."id" = f."challengeId"
            WHERE f."publicId" = @Id
            """,
            new { flow.Id });
        Assert.NotEqual(keys.FlowId, keys.ChallengeId);
        Assert.Equal(keys.ChallengeId, keys.ChallengeForeignKey);
        await Assert.ThrowsAnyAsync<DbException>(() => connection.ExecuteAsync(
            """
            UPDATE "identity"."loginVerificationFlows" SET "publicId"=@Replacement WHERE "publicId"=@Id
            """,
            new { Replacement = Guid.NewGuid(), flow.Id }));
        await Assert.ThrowsAnyAsync<DbException>(() => connection.ExecuteAsync(
            """
            UPDATE "identity"."loginVerificationFlows"
            SET "userId" =(
                SELECT "users"."id" AS "row_id"
                FROM "identity"."users" AS "users"
                WHERE "users"."publicId" = @Other)
            WHERE "publicId" = @Id
            """,
            new { Other = OtherCustomer, flow.Id }));
        var grant = await login.CompleteOtp(flow.Id, flow.Secret, "test-agent", "123456", false, default);
        Assert.True(grant.OtpVerified);
        Assert.True(await _repository.IsSessionActive(grant.SessionId, DateTimeOffset.UtcNow, default));
        await Assert.ThrowsAsync<DisputePortal.Api.Models.Exceptions.PortalValidationException>(() => login.CompleteOtp(
            flow.Id,
            flow.Secret,
            "test-agent",
            "123456",
            false,
            default));
    }
}
