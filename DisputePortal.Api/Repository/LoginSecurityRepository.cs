using DisputePortal.Api.Configuration;
using System.Data.Common;
using System.Security.Cryptography;
using Dapper;
using DisputePortal.Api.Models.Exceptions;
using DisputePortal.Api.Models.Responses;
using DisputePortal.Api.Models.Domain;
using DisputePortal.Api.Interfaces.Data;
using DisputePortal.Api.Interfaces.Repositories;
using DisputePortal.Api.Interfaces.Security;
using DisputePortal.Platform.Infrastructure.Sql;

namespace DisputePortal.Api.Repository;
// Login proofs, session issuance and device rotation share the business lock and one SQL commit.
public class LoginSecurityRepository(
    IDatabaseConnectionFactory factory,
    IDatabaseDialect databaseDialect,
    ISecretProtector protector,
    IOtpHasher hasher,
    LoginVerificationPolicy policy,
    SessionPolicy sessions,
    ApiRuntimeOptions runtime) : RepositoryBase(factory, databaseDialect), ILoginSecurityRepository
{
    private static readonly string DeviceSql = SqlScripts.ReadSqlScript("Shared/Queries/Api/LoginSecurityRepository/GetTrustedLoginDevicesForUser");
    private static readonly string FlowSql = SqlScripts.ReadSqlScript("Shared/Queries/Api/LoginSecurityRepository/GetLoginVerificationFlows");

    private bool MatchesSecret(Guid id, string value, string expected) => CryptographicOperations.FixedTimeEquals(
        System.Text.Encoding.UTF8.GetBytes(hasher.Hash(id, value)),
        System.Text.Encoding.UTF8.GetBytes(expected));

    private string Stamp(Guid user, string passwordHash) => hasher.Hash(user, "login-password:" + passwordHash);

    private string Agent(Guid user, string agent) => hasher.Hash(user, "login-agent:" + agent[..Math.Min(agent.Length, 512)]);

    private static string Secret() => LoginDeviceToken.NewSecret();

    private static bool TryCookie(string? value, out Guid id, out string secret) => LoginDeviceToken.TryParse(value, out id, out secret);

    private static string Cookie(Guid id, string secret) => LoginDeviceToken.Serialize(id, secret);

    private async Task<UserState> Current(DbConnection c, DbTransaction tx, Guid user, CancellationToken ct)
    {
        var row = await c.QuerySingleOrDefaultAsync<UserState>(new CommandDefinition(
            """
            SELECT
                "users"."passwordHash" AS PasswordHash,
                "users"."accountStatus" AS Status,
                "users"."loginOtpEnabled" AS LoginOtpEnabled
            FROM "identity"."users" AS "users"
            WHERE "users"."publicId" = @Id
            """,
            new { Id = user },
            tx,
            cancellationToken: ct));
        if (row is null || row.Status != "Active")
        {
            throw new PortalValidationException("Your security details changed. Sign in again.");
        }

        return row;
    }

    private async Task<Guid> Session(DbConnection c, DbTransaction tx, Guid user, CancellationToken ct)
    {
        var id = Guid.NewGuid();
        await c.ExecuteAsync(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/LoginSecurityRepository/InsertUserSession"),
            new
            {
                Id = id,
                User = user,
                Expires = DateTimeOffset.UtcNow.AddMinutes(sessions.AbsoluteMinutes)
            },
            tx,
            cancellationToken: ct));
        return id;
    }

    public async Task<LoginSessionGrant?> TryPasswordSession(
        Guid user,
        string expectedPasswordHash,
        string? deviceCookie,
        string userAgent,
        CancellationToken ct)
    {
        await using var c = await connections.OpenConnection(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await LockOperations(c, tx, ct);
        var current = await Current(c, tx, user, ct);
        if (current.PasswordHash != expectedPasswordHash)
        {
            throw new PortalValidationException("Your security details changed. Sign in again.");
        }

        var roles = await Roles(c, tx, user, ct);
        if (!policy.RequiresCode(roles, current.LoginOtpEnabled))
        {
            var direct = await Session(c, tx, user, ct);
            await tx.CommitAsync(ct);
            return new(user, direct, false);
        }

        if (!policy.CanRemember(roles, current.LoginOtpEnabled) || !TryCookie(deviceCookie, out var id, out var secret))
        {
            await tx.CommitAsync(ct);
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        var row = await c.QuerySingleOrDefaultAsync<Device>(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/LoginSecurityRepository/GetOwnedTrustedLoginDeviceById"),
            new
            {
                User = user,
                Now = now,
                Id = id
            },
            tx,
            cancellationToken: ct));
        if (row is null
            || row.CreatedAt.AddDays(policy.RememberedDeviceDays) <= now
            || !MatchesSecret(id, "device:" + secret, row.TokenHash)
            || row.PasswordStamp != Stamp(user, current.PasswordHash)
            || row.AgentHash != Agent(user, userAgent))
        {
            await tx.CommitAsync(ct);
            return null;
        }

        var rotated = Secret();
        await c.ExecuteAsync(new CommandDefinition(
            """
            UPDATE "identity"."trustedLoginDevices"
            SET
                "tokenHash" = @Hash,
                "lastUsedAt" = @Now
            WHERE "trustedLoginDevices"."publicId" = @Id
            """,
            new
            {
                Id = id,
                Hash = hasher.Hash(id, "device:" + rotated),
                Now = now
            },
            tx,
            cancellationToken: ct));
        var session = await Session(c, tx, user, ct);
        await AuditOperation(c, tx, user, "RememberedDeviceUsed", id, new { }, ct);
        await tx.CommitAsync(ct);
        return new(user, session, false, Cookie(id, rotated), row.ExpiresAt, true);
    }

    public async Task<LoginFlowInfo> CreateFlow(
        Guid user,
        Guid challenge,
        string expectedPasswordHash,
        string flowSecret,
        string userAgent,
        string destination,
        bool verifyRegisteredContact,
        CancellationToken ct)
    {
        await using var c = await connections.OpenConnection(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await LockOperations(c, tx, ct);
        var current = await Current(c, tx, user, ct);
        if (current.PasswordHash != expectedPasswordHash)
        {
            throw new PortalValidationException("Your security details changed. Sign in again.");
        }

        var now = DateTimeOffset.UtcNow;
        if (await c.ExecuteScalarAsync<int>(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/LoginSecurityRepository/CountLoginVerificationFlowsForUser"),
            new { User = user, Since = now.AddMinutes(-runtime.LoginFlowLifetimeMinutes) },
            tx,
            cancellationToken: ct)) >= 5)
        {
            throw new PortalConflictException("Too many verification requests. Try again in 15 minutes.");
        }

        if (await c.ExecuteScalarAsync<int>(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/LoginSecurityRepository/CountPendingIdentityChallengesForUser"),
            new
            {
                Id = challenge,
                User = user,
                Now = now
            },
            tx,
            cancellationToken: ct)) != 1)
        {
            throw new PortalValidationException("Start sign-in again.");
        }

        var info = new LoginFlowInfo(
            now.AddSeconds(runtime.VerificationResendDelaySeconds),
            now.AddMinutes(runtime.LoginFlowLifetimeMinutes),
            policy.CanRemember(await Roles(c, tx, user, ct), current.LoginOtpEnabled),
            policy.RememberedDeviceDays);
        await c.ExecuteAsync(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/LoginSecurityRepository/InsertLoginVerificationFlow"),
            new
            {
                Id = challenge,
                User = user,
                Flow = hasher.Hash(challenge, "flow:" + flowSecret),
                Password = Stamp(user, current.PasswordHash),
                Agent = Agent(user, userAgent),
                Destination = protector.Protect(destination),
                Verify = verifyRegisteredContact,
                Now = now,
                Expires = info.FlowExpiresAt,
                Resend = info.ResendAvailableAt
            },
            tx,
            cancellationToken: ct));
        await tx.CommitAsync(ct);
        return info;
    }

    private async Task<Flow> CheckFlow(DbConnection c, DbTransaction tx, Guid id, string secret, string agent, CancellationToken ct)
    {
        var row = await c.QuerySingleOrDefaultAsync<Flow>(new CommandDefinition(FlowSql, new { Id = id }, tx, cancellationToken: ct));
        if (row is null
            || row.Status != "Active"
            || row.ExpiresAt <= DateTimeOffset.UtcNow
            || row.State is not ("Ready" or "Pending")
            || row.Attempts >= 5
            || !MatchesSecret(id, "flow:" + secret, row.FlowHash)
            || row.PasswordStamp != Stamp(row.UserId, row.PasswordHash)
            || row.AgentHash != Agent(row.UserId, agent))
        {
            throw new PortalValidationException("This verification is unavailable. Sign in again.");
        }

        return row;
    }

    public async Task<LoginResend> ReserveResend(Guid challenge, string flowSecret, string userAgent, string code, CancellationToken ct)
    {
        await using var c = await connections.OpenConnection(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await LockOperations(c, tx, ct);
        var row = await CheckFlow(c, tx, challenge, flowSecret, userAgent, ct);
        var now = DateTimeOffset.UtcNow;
        if (row.ResendAvailableAt > now)
        {
            throw new PortalConflictException($"Please wait {runtime.VerificationResendDelaySeconds} seconds before requesting another code.");
        }

        if (row.ResendCount >= 5)
        {
            throw new PortalConflictException("For your security, start sign-in again to request another code.");
        }

        var expiry = now.AddMinutes(runtime.VerificationCodeLifetimeMinutes) < row.ExpiresAt ? now.AddMinutes(runtime.VerificationCodeLifetimeMinutes) : row.ExpiresAt;
        await c.ExecuteAsync(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/LoginSecurityRepository/MarkIdentityChallengesPending"),
            new
            {
                Id = challenge,
                Hash = hasher.Hash(challenge, code),
                Expires = expiry,
                Resend = now.AddSeconds(runtime.VerificationResendDelaySeconds)
            },
            tx,
            cancellationToken: ct));
        await AuditOperation(c, tx, row.UserId, "LoginCodeResent", row.UserId, new { row.Channel }, ct);
        await tx.CommitAsync(ct);
        return new(
            row.UserId,
            row.Channel,
            protector.Unprotect(row.ProtectedDestination),
            expiry,
            row.Revision + 1,
            new(
                now.AddSeconds(runtime.VerificationResendDelaySeconds),
                row.ExpiresAt,
                policy.CanRemember(await Roles(c, null, row.UserId, ct), await Preference(c, null, row.UserId, ct)),
                policy.RememberedDeviceDays));
    }

    public async Task MarkDelivered(Guid challenge, int revision, CancellationToken ct)
    {
        await using var c = await connections.OpenConnection(ct);
        await c.ExecuteAsync(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/LoginSecurityRepository/MarkIdentityChallengesReady"),
            new { Id = challenge, Revision = revision },
            cancellationToken: ct));
    }

    public async Task<LoginSessionGrant> CompleteOtp(
        Guid challenge,
        string flowSecret,
        string userAgent,
        string code,
        bool remember,
        CancellationToken ct)
    {
        await using var c = await connections.OpenConnection(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await LockOperations(c, tx, ct);
        var row = await CheckFlow(c, tx, challenge, flowSecret, userAgent, ct);
        if (row.State != "Ready" || row.CodeExpiresAt <= DateTimeOffset.UtcNow)
        {
            throw new PortalValidationException("This code has expired. Request a new code.");
        }

        if (!hasher.Matches(challenge, code, row.CodeHash))
        {
            await c.ExecuteAsync(new CommandDefinition(
                """
                UPDATE "identity"."identityChallenges"
                SET
                    "attempts" = "identityChallenges"."attempts" + 1
                WHERE "identityChallenges"."publicId" = @Id
                """,
                new { Id = challenge },
                tx,
                cancellationToken: ct));
            await tx.CommitAsync(ct);
            throw new PortalValidationException("Check your six-digit code and try again.");
        }

        await c.ExecuteAsync(new CommandDefinition(
            """
            UPDATE "identity"."identityChallenges"
            SET
                "state" = 'Used',
                "protectedPayload" = ''
            WHERE "identityChallenges"."publicId" = @Id
            """,
            new { Id = challenge },
            tx,
            cancellationToken: ct));
        if (row.VerifyRegisteredContact)
        {
            var destination = protector.Unprotect(row.ProtectedDestination);
            if (!string.Equals(destination, row.LoginIdentifier, StringComparison.OrdinalIgnoreCase))
            {
                throw new PortalValidationException("Your contact details changed. Sign in again.");
            }

            var sql = row.Channel == "email" ? SqlScripts.RenderSqlScript(
                "Shared/Queries/Api/LoginSecurityRepository/UpdateCustomerProfilesForUserWithEmailVerifiedAndPreferredOtpChannel",
                ("TrueValue", dialect.TrueLiteral)) : SqlScripts.RenderSqlScript(
                "Shared/Queries/Api/LoginSecurityRepository/UpdateCustomerProfilesForUserWithPhoneVerifiedAndPreferredOtpChannel",
                ("TrueValue", dialect.TrueLiteral));
            if (await c.ExecuteAsync(new CommandDefinition(
                sql,
                new
                {
                    User = row.UserId,
                    Destination = destination,
                    row.Channel
                },
                tx,
                cancellationToken: ct)) != 1)
            {
                throw new PortalValidationException("Your contact details changed. Sign in again.");
            }
        }

        string? cookie = null;
        DateTimeOffset? expires = null;
        if (remember && policy.CanRemember(await Roles(c, tx, row.UserId, ct), row.LoginOtpEnabled))
        {
            var active = (await c.QueryAsync<Device>(new CommandDefinition(
                SqlScripts.ReadSqlScript("Shared/Queries/Api/LoginSecurityRepository/GetLoginSecurityPage"),
                new { User = row.UserId, Now = DateTimeOffset.UtcNow },
                tx,
                cancellationToken: ct))).ToArray();
            foreach (var old in active.Skip(policy.MaximumDevices - 1))
                await c.ExecuteAsync(new CommandDefinition(
                """
                UPDATE "identity"."trustedLoginDevices"
                SET
                    "revokedAt" = @Now
                WHERE "trustedLoginDevices"."publicId" = @Id
                """,
                new { old.Id, Now = DateTimeOffset.UtcNow },
                tx,
                cancellationToken: ct));
            var id = Guid.NewGuid();
            var secret = Secret();
            var now = DateTimeOffset.UtcNow;
            expires = now.AddDays(policy.RememberedDeviceDays);
            var browser = userAgent.Contains("Edg/", StringComparison.Ordinal) ? "Edge" : userAgent.Contains(
                "Firefox/",
                StringComparison.Ordinal) ? "Firefox" : userAgent.Contains("Chrome/", StringComparison.Ordinal) ? "Chrome" : userAgent.Contains(
                "Safari/",
                StringComparison.Ordinal) ? "Safari" : "Browser";
            var device = userAgent.Contains("Mobile", StringComparison.OrdinalIgnoreCase)
                || userAgent.Contains("iPhone", StringComparison.OrdinalIgnoreCase) ? "Mobile" : userAgent.Contains(
                "iPad",
                StringComparison.OrdinalIgnoreCase) ? "Tablet" : "Computer";
            await c.ExecuteAsync(new CommandDefinition(
                SqlScripts.ReadSqlScript("Shared/Queries/Api/LoginSecurityRepository/InsertTrustedLoginDevice"),
                new
                {
                    Id = id,
                    User = row.UserId,
                    Hash = hasher.Hash(id, "device:" + secret),
                    Password = Stamp(row.UserId, row.PasswordHash),
                    Agent = Agent(row.UserId, userAgent),
                    Device = device,
                    Browser = browser,
                    Now = now,
                    Expires = expires
                },
                tx,
                cancellationToken: ct));
            await AuditOperation(c, tx, row.UserId, "LoginDeviceRemembered", id, new { Days = policy.RememberedDeviceDays }, ct);
            cookie = Cookie(id, secret);
        }

        var session = await Session(c, tx, row.UserId, ct);
        await tx.CommitAsync(ct);
        return new(row.UserId, session, true, cookie, expires);
    }

    public async Task<LoginDeviceSettingsDto> GetDevices(Guid user, string? currentCookie, CancellationToken ct)
    {
        await using var c = await connections.OpenConnection(ct);
        var roles = await Roles(c, null, user, ct);
        var preference = await Preference(c, null, user, ct);
        var current = TryCookie(currentCookie, out var id, out _) ? id : Guid.Empty;
        var rows = (await c.QueryAsync<Device>(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/LoginSecurityRepository/GetLoginSecurityPage"),
            new { User = user, Now = DateTimeOffset.UtcNow },
            cancellationToken: ct))).ToArray();
        return new(
            policy.CanRemember(roles, preference),
            policy.RequiresCode(roles, preference),
            policy.RememberedDeviceDays,
            rows.Select(x => new LoginDeviceDto(x.Id, x.DeviceType, x.Browser, x.CreatedAt, x.ExpiresAt, x.LastUsedAt, x.Id == current)).ToArray(),
            policy.CanChangePreference(roles),
            preference,
            policy.PreferencePolicyMessage(roles));
    }

    public async Task RevokeDevice(Guid user, Guid? device, CancellationToken ct)
    {
        await using var c = await connections.OpenConnection(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await LockOperations(c, tx, ct);
        await c.ExecuteAsync(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/LoginSecurityRepository/RevokeTrustedLoginDevicesForUserWithRevokedAt"),
            new
            {
                User = user,
                Id = device,
                Now = DateTimeOffset.UtcNow
            },
            tx,
            cancellationToken: ct));
        await AuditOperation(c, tx, user, "RememberedDeviceRevoked", device ?? user, new { AllDevices = device is null }, ct);
        await tx.CommitAsync(ct);
    }

    private static Task<bool?> Preference(DbConnection c, DbTransaction? tx, Guid user, CancellationToken ct) =>
        c.QuerySingleOrDefaultAsync<bool?>(new CommandDefinition(
        """
        SELECT
            "users"."loginOtpEnabled" AS "login_otp_enabled"
        FROM "identity"."users" AS "users"
        WHERE "users"."publicId" = @Id
        """,
        new { Id = user },
        tx,
        cancellationToken: ct));

    public async Task<bool?> GetLoginOtpPreference(Guid user, CancellationToken ct)
    {
        await using var c = await connections.OpenConnection(ct);
        return await Preference(c, null, user, ct);
    }

    public async Task SetLoginVerification(Guid user, string expectedPasswordHash, bool enabled, CancellationToken ct)
    {
        await using var c = await connections.OpenConnection(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await LockOperations(c, tx, ct);
        var current = await Current(c, tx, user, ct);
        if (current.PasswordHash != expectedPasswordHash)
        {
            throw new PortalConflictException("Your security details changed. Sign in again before updating verification.");
        }

        var roles = await Roles(c, tx, user, ct);
        if (!policy.CanChangePreference(roles))
        {
            throw new PortalForbiddenException(policy.PreferencePolicyMessage(roles) ?? "Your sign-in verification is managed by your administrator.");
        }

        var now = DateTimeOffset.UtcNow;
        var changed = await c.ExecuteAsync(new CommandDefinition(
            """
            UPDATE "identity"."users"
            SET
                "loginOtpEnabled" = @Enabled,
                "updatedAt" = @Now
            WHERE "users"."publicId" = @User
            AND "users"."passwordHash" = @Expected
            AND "users"."accountStatus" = 'Active'
            """,
            new
            {
                User = user,
                Enabled = enabled,
                Now = now,
                Expected = expectedPasswordHash
            },
            tx,
            cancellationToken: ct));
        if (changed != 1)
        {
            throw new PortalConflictException("Your security details changed. Sign in again before updating verification.");
        }

        await c.ExecuteAsync(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/LoginSecurityRepository/RevokeTrustedDevicesAndPendingLoginChallenges"),
            new { User = user, Now = now },
            tx,
            cancellationToken: ct));
        await AuditOperation(
            c,
            tx,
            user,
            "LoginVerificationChanged",
            user,
            new
            {
                Enabled = enabled,
                PreviousPreference = current.LoginOtpEnabled,
                RememberedDevices = "Revoked"
            },
            ct);
        await tx.CommitAsync(ct);
    }

    private class UserState
    {
        public string PasswordHash { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public bool? LoginOtpEnabled { get; set; }

        public UserState()
        {
        }

        public UserState(string PasswordHash, string Status, bool? LoginOtpEnabled)
        {
            this.PasswordHash = PasswordHash;
            this.Status = Status;
            this.LoginOtpEnabled = LoginOtpEnabled;
        }
    }

    private class Flow
    {
        public Guid UserId { get; set; }
        public string FlowHash { get; set; } = "";
        public string PasswordStamp { get; set; } = "";
        public string AgentHash { get; set; } = "";
        public string PasswordHash { get; set; } = "";
        public string Status { get; set; } = "";
        public bool? LoginOtpEnabled { get; set; }
        public string LoginIdentifier { get; set; } = "";
        public string ProtectedDestination { get; set; } = "";
        public bool VerifyRegisteredContact { get; set; }
        public DateTimeOffset ExpiresAt { get; set; }
        public DateTimeOffset CodeExpiresAt { get; set; }
        public DateTimeOffset ResendAvailableAt { get; set; }
        public string Channel { get; set; } = "";
        public string State { get; set; } = "";
        public string CodeHash { get; set; } = "";
        public int Attempts { get; set; }
        public int ResendCount { get; set; }
        public int Revision { get; set; }
    }

    private class Device
    {
        public Guid Id { get; set; }
        public string TokenHash { get; set; } = "";
        public string PasswordStamp { get; set; } = "";
        public string AgentHash { get; set; } = "";
        public string DeviceType { get; set; } = "";
        public string Browser { get; set; } = "";
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset ExpiresAt { get; set; }
        public DateTimeOffset LastUsedAt { get; set; }
    }
}
