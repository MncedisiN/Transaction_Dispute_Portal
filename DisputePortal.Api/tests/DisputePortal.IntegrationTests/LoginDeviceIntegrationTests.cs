using Dapper;
using DisputePortal.Api.Configuration;
using DisputePortal.Api.Service.Security;
using DisputePortal.Api.Models.Exceptions;
using DisputePortal.Api.Models.Domain;
using DisputePortal.Api.Repository;
using DisputePortal.Api.Repository.Data;
using Xunit;

namespace DisputePortal.IntegrationTests;

public sealed partial class DisputeWorkflowRepositoryTests
{
    private LoginSecurityRepository LoginSecurity(LoginVerificationPolicy? policy = null) => new(
        _connections,
        DatabaseDialect.GetDatabaseDialect(_provider),
        new TestProtector(),
        new OtpHasher(new InfobipOptions { OtpHashKey = new string('t', 64) }),
        policy ?? new global::DisputePortal.Api.Models.Domain.LoginVerificationPolicy(),
        new(),
        DisputePortal.TestFixtures.TestRuntimeConfiguration.Read());

    private async Task<(Guid Id, string Secret)> LoginFlow(LoginSecurityRepository login, Guid user, bool verifyContact = false)
    {
        var destination = user == Customer ? "customer@test.local" : "agent@test.local";
        var challenge = await _repository.CreateIdentityChallenge(user, "Login", "email", destination, "", "123456", CancellationToken.None);
        var secret = LoginDeviceToken.NewSecret();
        await login.CreateFlow(
            user,
            challenge.ChallengeId,
            "not-used",
            secret,
            "test-agent",
            destination,
            verifyContact,
            CancellationToken.None);
        await login.MarkDelivered(challenge.ChallengeId, 0, CancellationToken.None);
        return (challenge.ChallengeId, secret);
    }

    [Fact]
    public async Task Login_device_requires_correct_otp_and_password_and_rotates_and_revokes()
    {
        var ct = CancellationToken.None;
        var login = LoginSecurity();
        var flow = await LoginFlow(login, Customer);
        Assert.Null(await login.TryPasswordSession(Customer, "not-used", null, "test-agent", ct));
        await Assert.ThrowsAsync<PortalValidationException>(() => login.CompleteOtp(flow.Id, "wrong", "test-agent", "123456", true, ct));
        await Assert.ThrowsAsync<PortalValidationException>(() => login.CompleteOtp(flow.Id, flow.Secret, "test-agent", "000000", true, ct));
        var grant = await login.CompleteOtp(flow.Id, flow.Secret, "test-agent", "123456", true, ct);
        Assert.True(grant.OtpVerified);
        Assert.NotNull(grant.DeviceCookie);
        Assert.True(await _repository.IsSessionActive(grant.SessionId, DateTimeOffset.UtcNow, ct));
        Assert.True(LoginDeviceToken.TryParse(grant.DeviceCookie, out var device, out var rawSecret));
        await using var c = await _connections.OpenConnection(ct);
        var stored = await c.ExecuteScalarAsync<string>(
            """
      SELECT "trusted_login_devices"."tokenHash" AS "token_hash"
      FROM "identity"."trustedLoginDevices" AS "trusted_login_devices"
      WHERE "trusted_login_devices"."publicId" = @Id
      """,
            new { Id = device });
        Assert.DoesNotContain(rawSecret, stored!);
        await Assert.ThrowsAsync<PortalValidationException>(() => login.TryPasswordSession(
            Customer,
            "old-password-hash",
            grant.DeviceCookie,
            "test-agent",
            ct));
        Assert.Null(await login.TryPasswordSession(OtherCustomer, "not-used", grant.DeviceCookie, "test-agent", ct));
        Assert.Null(await login.TryPasswordSession(Customer, "not-used", grant.DeviceCookie, "other-agent", ct));
        var next = await login.TryPasswordSession(Customer, "not-used", grant.DeviceCookie, "test-agent", ct);
        Assert.True(next!.RememberedDeviceUsed);
        Assert.NotEqual(grant.DeviceCookie, next.DeviceCookie);
        Assert.Null(await login.TryPasswordSession(Customer, "not-used", grant.DeviceCookie, "test-agent", ct));
        Assert.True(Assert.Single((await login.GetDevices(Customer, next.DeviceCookie, ct)).Devices).Current);
        Assert.Empty((await login.GetDevices(OtherCustomer, next.DeviceCookie, ct)).Devices);
        await login.RevokeDevice(OtherCustomer, device, ct);
        Assert.Single((await login.GetDevices(Customer, next.DeviceCookie, ct)).Devices);
        await login.RevokeDevice(Customer, device, ct);
        Assert.Null(await login.TryPasswordSession(Customer, "not-used", next.DeviceCookie, "test-agent", ct));
        await Assert.ThrowsAsync<PortalValidationException>(() => login.CompleteOtp(flow.Id, flow.Secret, "test-agent", "123456", true, ct));
    }

    [Theory]
    [InlineData("password")]
    [InlineData("contact")]
    [InlineData("role")]
    [InlineData("status")]
    public async Task Login_device_and_pending_flow_are_revoked_when_security_details_change(string change)
    {
        var ct = CancellationToken.None;
        var login = LoginSecurity();
        var flow = await LoginFlow(login, Customer);
        var grant = await login.CompleteOtp(flow.Id, flow.Secret, "test-agent", "123456", true, ct);
        await using var c = await _connections.OpenConnection(ct);
        await c.ExecuteAsync(
            """
      UPDATE "identity"."identityChallenges"
      SET "createdAt" = @Old
      WHERE "identityChallenges"."publicId" = @Id
      """,
            new { Id = flow.Id, Old = DateTimeOffset.UtcNow.AddMinutes(-2) });
        var pending = await LoginFlow(login, Customer);
        var sql = change switch
        {
            "password" => """
      UPDATE "identity"."users"
      SET "passwordHash" = 'changed'
      WHERE "users"."publicId" = @User
      """,
            "contact" => """
      UPDATE "identity"."customerProfiles"
      SET "email" = 'new@test.local'
      WHERE(
          SELECT "related1"."publicId"
          FROM "identity"."users" AS "related1"
          WHERE "related1"."id" = "customerProfiles"."userId") = @User
      """,
            "role" => """
      INSERT INTO "identity"."userRoles"("userId",
          "roleId")
      SELECT(
          SELECT "resolved1"."id"
          FROM "identity"."users" AS "resolved1"
          WHERE "resolved1"."publicId" = @User),
      "roles"."id"
      FROM "identity"."roles" AS "roles"
      WHERE "roles"."code" = 'audit_reader'
      """,
            _ => """
      UPDATE "identity"."users"
      SET "accountStatus" = 'Locked'
      WHERE "users"."publicId" = @User
      """
        };
        await c.ExecuteAsync(sql, new { User = Customer });
        Assert.Empty((await login.GetDevices(Customer, grant.DeviceCookie, ct)).Devices);
        await Assert.ThrowsAsync<PortalValidationException>(() => login.CompleteOtp(
            pending.Id,
            pending.Secret,
            "test-agent",
            "123456",
            true,
            ct));
        if (change != "status")
        {
            Assert.Null(await login.TryPasswordSession(
                Customer,
                change == "password" ? "changed" : "not-used",
                grant.DeviceCookie,
                "test-agent",
                ct));
        }
    }

    [Fact]
    public async Task Login_resend_has_server_cooldown_revision_protection_and_cumulative_attempts()
    {
        var ct = CancellationToken.None;
        var login = LoginSecurity();
        var flow = await LoginFlow(login, Customer);
        await Assert.ThrowsAsync<PortalConflictException>(() => login.ReserveResend(flow.Id, flow.Secret, "test-agent", "654321", ct));
        await using var c = await _connections.OpenConnection(ct);
        for (var i = 0; i < 3; i++)
            await Assert.ThrowsAsync<PortalValidationException>(() => login.CompleteOtp(
            flow.Id,
            flow.Secret,
            "test-agent",
            "000000",
            false,
            ct));
        await c.ExecuteAsync(
            "UPDATE \"identity\".\"loginVerificationFlows\" SET \"resendAvailableAt\"=@Past WHERE \"publicId\"=@Id",
            new { Id = flow.Id, Past = DateTimeOffset.UtcNow.AddMinutes(-1) });
        var resend = await login.ReserveResend(flow.Id, flow.Secret, "test-agent", "654321", ct);
        Assert.Equal(1, resend.Revision);
        await login.MarkDelivered(flow.Id, 0, ct);
        await Assert.ThrowsAsync<PortalValidationException>(() => login.CompleteOtp(flow.Id, flow.Secret, "test-agent", "654321", false, ct));
        await login.MarkDelivered(flow.Id, resend.Revision, ct);
        await Assert.ThrowsAsync<PortalValidationException>(() => login.CompleteOtp(flow.Id, flow.Secret, "test-agent", "123456", false, ct));
        await Assert.ThrowsAsync<PortalValidationException>(() => login.CompleteOtp(flow.Id, flow.Secret, "test-agent", "000000", false, ct));
        await Assert.ThrowsAsync<PortalValidationException>(() => login.CompleteOtp(flow.Id, flow.Secret, "test-agent", "654321", false, ct));
        Assert.Equal(
            5,
            await c.ExecuteScalarAsync<int>(
                """
      SELECT "identity_challenges"."attempts" AS "attempts"
      FROM "identity"."identityChallenges" AS "identity_challenges"
      WHERE "identity_challenges"."publicId" = @Id
      """,
                new { Id = flow.Id }));
    }

    [Fact]
    public async Task Login_staff_cannot_remember_devices_and_first_contact_is_verified_before_session_creation()
    {
        var ct = CancellationToken.None;
        var login = LoginSecurity(new LoginVerificationPolicy
        {
            LoginOtpEnabled = false,
            RequireStaffMfa = true
        });
        var flow = await LoginFlow(login, Agent, true);
        var grant = await login.CompleteOtp(flow.Id, flow.Secret, "test-agent", "123456", true, ct);
        Assert.Null(grant.DeviceCookie);
        Assert.True(grant.OtpVerified);
        Assert.True(await _repository.IsSessionActive(grant.SessionId, DateTimeOffset.UtcNow, ct));
        Assert.True((await _repository.FindIdentity("agent@test.local", ct))!.EmailVerified);
        Assert.Null(await login.TryPasswordSession(Agent, "not-used", null, "test-agent", ct));
        var customer = await login.TryPasswordSession(Customer, "not-used", null, "test-agent", ct);
        Assert.NotNull(customer);
        Assert.False(customer.OtpVerified);
    }

    [Fact]
    public async Task Login_expiry_and_nonsecurity_profile_edits_do_not_extend_trust()
    {
        var ct = CancellationToken.None;
        var login = LoginSecurity();
        var flow = await LoginFlow(login, Customer);
        var grant = await login.CompleteOtp(flow.Id, flow.Secret, "test-agent", "123456", true, ct);
        await using var c = await _connections.OpenConnection(ct);
        await c.ExecuteAsync(
            """
      UPDATE "identity"."customerProfiles"
      SET "displayName" = 'Changed name'
      WHERE(
          SELECT "related1"."publicId"
          FROM "identity"."users" AS "related1"
          WHERE "related1"."id" = "customerProfiles"."userId") = @User
      """,
            new { User = Customer });
        var next = await login.TryPasswordSession(Customer, "not-used", grant.DeviceCookie, "test-agent", ct);
        Assert.NotNull(next); // PostgreSQL persists microseconds; comparing that precision proves rotation did not extend expiry.
        Assert.Equal(grant.DeviceExpiresAt!.Value.UtcTicks / 10, next.DeviceExpiresAt!.Value.UtcTicks / 10);
        await c.ExecuteAsync(
            """
      UPDATE "identity"."trustedLoginDevices"
      SET "expiresAt" = @Past
      WHERE(
          SELECT "related1"."publicId"
          FROM "identity"."users" AS "related1"
          WHERE "related1"."id" = "trustedLoginDevices"."userId") = @User
      """,
            new { User = Customer, Past = DateTimeOffset.UtcNow.AddMinutes(-1) });
        Assert.Null(await login.TryPasswordSession(Customer, "not-used", next.DeviceCookie, "test-agent", ct));
    }
}
