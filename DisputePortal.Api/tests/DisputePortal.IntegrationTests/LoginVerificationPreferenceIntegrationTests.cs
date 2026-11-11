using Dapper;
using DisputePortal.Api.Models.Domain;
using DisputePortal.Api.Models.Exceptions;
using Xunit;

namespace DisputePortal.IntegrationTests;

public sealed partial class DisputeWorkflowRepositoryTests
{
    [Fact]
    public async Task Login_preference_changes_revoke_trust_and_login_flows_but_preserve_recovery_codes_and_roles()
    {
        var ct = CancellationToken.None;
        var login = LoginSecurity();
        var flow = await LoginFlow(login, Customer);
        var remembered = await login.CompleteOtp(flow.Id, flow.Secret, "test-agent", "123456", true, ct);
        await using var c = await _connections.OpenConnection(ct);
        await c.ExecuteAsync(
            """
      UPDATE "identity"."identityChallenges"
      SET "createdAt" = @Old
      WHERE "identityChallenges"."publicId" = @Id
      """,
            new { Id = flow.Id, Old = DateTimeOffset.UtcNow.AddMinutes(-2) });
        var pending = await LoginFlow(login, Customer);
        var recovery = await _repository.CreateIdentityChallenge(
            Customer,
            "Recovery",
            "email",
            "customer@test.local",
            "recovery-payload",
            "654321",
            ct);
        await _repository.MarkIdentityChallengeReady(recovery.ChallengeId, ct);
        var roleCount = await c.ExecuteScalarAsync<int>(
            """
      SELECT COUNT(*)
      FROM "identity"."userRoles" AS "user_roles"
      WHERE(
          SELECT "related1"."publicId"
          FROM "identity"."users" AS "related1"
          WHERE "related1"."id" = "user_roles"."userId") = @User
      """,
            new { User = Customer });
        Assert.Null(await login.GetLoginOtpPreference(Customer, ct));
        await Assert.ThrowsAsync<PortalConflictException>(() => login.SetLoginVerification(Customer, "outdated-hash", false, ct));
        await login.SetLoginVerification(Customer, "not-used", false, ct);
        var settings = await login.GetDevices(Customer, remembered.DeviceCookie, ct);
        Assert.True(settings.CanChangeVerification);
        Assert.False(settings.RequiresTwoStep);
        Assert.False(settings.CanRememberDevice);
        Assert.Empty(settings.Devices);
        Assert.Equal(false, await login.GetLoginOtpPreference(Customer, ct));
        await Assert.ThrowsAsync<PortalValidationException>(() => login.CompleteOtp(
            pending.Id,
            pending.Secret,
            "test-agent",
            "123456",
            false,
            ct));
        Assert.Equal(
            "Ready",
            await c.ExecuteScalarAsync<string>(
                """
      SELECT "identity_challenges"."state" AS "state"
      FROM "identity"."identityChallenges" AS "identity_challenges"
      WHERE "identity_challenges"."publicId" = @Id
      """,
                new { Id = recovery.ChallengeId }));
        Assert.Equal(
            roleCount,
            await c.ExecuteScalarAsync<int>(
                """
      SELECT COUNT(*)
      FROM "identity"."userRoles" AS "user_roles"
      WHERE(
          SELECT "related1"."publicId"
          FROM "identity"."users" AS "related1"
          WHERE "related1"."id" = "user_roles"."userId") = @User
      """,
                new { User = Customer }));
        Assert.Equal(
            1,
            await c.ExecuteScalarAsync<int>(
                """
      SELECT COUNT(*)
      FROM "audit"."auditEvents" AS "admin_audit_logs"
      WHERE(
          SELECT "related1"."publicId"
          FROM "identity"."users" AS "related1"
          WHERE "related1"."id" = "admin_audit_logs"."actorUserId") = @User
      AND "admin_audit_logs"."entityPublicId" = @User
      AND "admin_audit_logs"."action" = 'LoginVerificationChanged'
      """,
                new { User = Customer }));
        var direct = await login.TryPasswordSession(Customer, "not-used", remembered.DeviceCookie, "test-agent", ct);
        Assert.NotNull(direct);
        Assert.False(direct.OtpVerified);
        Assert.False(direct.RememberedDeviceUsed);
        await login.SetLoginVerification(Customer, "not-used", true, ct);
        Assert.True((await login.GetDevices(Customer, null, ct)).RequiresTwoStep);
        Assert.Null(await login.TryPasswordSession(Customer, "not-used", remembered.DeviceCookie, "test-agent", ct));
    }

    [Fact]
    public async Task Staff_preference_is_available_only_under_an_explicit_operator_policy()
    {
        var ct = CancellationToken.None;
        var mandatory = LoginSecurity();
        Assert.False((await mandatory.GetDevices(Agent, null, ct)).CanChangeVerification);
        await Assert.ThrowsAsync<PortalForbiddenException>(() => mandatory.SetLoginVerification(Agent, "not-used", false, ct));
        var configurable = LoginSecurity(new LoginVerificationPolicy { AllowStaffPreference = true });
        await configurable.SetLoginVerification(Agent, "not-used", false, ct);
        var settings = await configurable.GetDevices(Agent, null, ct);
        Assert.True(settings.CanChangeVerification);
        Assert.False(settings.RequiresTwoStep);
        Assert.False(settings.CanRememberDevice);
        Assert.NotNull(await configurable.TryPasswordSession(Agent, "not-used", null, "test-agent", ct));
        Assert.True((await mandatory.GetDevices(Agent, null, ct)).RequiresTwoStep);
        Assert.Null(await mandatory.TryPasswordSession(Agent, "not-used", null, "test-agent", ct));
    }
}
