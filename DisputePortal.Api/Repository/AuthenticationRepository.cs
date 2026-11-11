using Dapper;
using DisputePortal.Api.Models.Exceptions;
using DisputePortal.Api.Models.Responses;
using DisputePortal.Api.Models.Internal;
using DisputePortal.Api.Models.Domain;
using DisputePortal.Api.Interfaces.Data;
using DisputePortal.Api.Interfaces.Repositories;
using DisputePortal.Platform.Infrastructure.Sql;

namespace DisputePortal.Api.Repository;

public class AuthenticationRepository(
    IDatabaseConnectionFactory factory,
    IDatabaseDialect databaseDialect,
    SessionPolicy? sessionPolicy = null) : RepositoryBase(factory, databaseDialect), IAuthenticationRepository, IPasswordChangeRepository
{
    private SessionPolicy SessionSettings => sessionPolicy ?? new SessionPolicy();

    public async Task<LoginRecord?> FindLogin(string normalizedIdentifier, CancellationToken cancellationToken)
    {
        var sql = SqlScripts.RenderSqlScript("Shared/Queries/Api/AuthenticationRepository/GetUsers", ("TrueValue", dialect.TrueLiteral));
        await using var connection = await connections.OpenConnection(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<LoginRow>(new CommandDefinition(
            sql,
            new { NormalizedIdentifier = normalizedIdentifier },
            cancellationToken: cancellationToken));
        return row is null ? null : new LoginRecord(
            row.UserId,
            row.LoginIdentifier,
            row.PasswordHash,
            row.AccountStatus,
            row.DisplayName,
            SplitAggregate(row.Roles),
            SplitAggregate(row.Permissions));
    }

    public async Task<Guid> CreateSession(Guid userId, DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        var sql = SqlScripts.ReadSqlScript("Shared/Queries/Api/AuthenticationRepository/InsertUserSession");
        await using var connection = await connections.OpenConnection(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            Id = id,
            UserId = userId,
            ExpiresAt = expiresAt
        }, cancellationToken: cancellationToken));
        return id;
    }

    public async Task<bool> IsSessionActive(Guid sessionId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var sql = SqlScripts.ReadSqlScript("Shared/Queries/Api/AuthenticationRepository/UpdateUserSessionsForSession");
        await using var connection = await connections.OpenConnection(cancellationToken);
        return await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                SessionId = sessionId,
                Now = now,
                IdleCutoff = now.AddMinutes(-SessionSettings.IdleMinutes),
                AbsoluteCutoff = now.AddMinutes(-SessionSettings.AbsoluteMinutes)
            },
            cancellationToken: cancellationToken)) == 1;
    }

    public async Task RevokeSession(Guid sessionId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenConnection(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE "identity"."userSessions"
            SET
                "revokedAt" = @Now
            WHERE "userSessions"."publicId" = @Id
            AND "userSessions"."revokedAt" IS NULL
            """,
            new { Id = sessionId, Now = now },
            cancellationToken: cancellationToken));
    }

    public async Task<CurrentUserDto?> GetCurrentUser(Guid userId, CancellationToken cancellationToken)
    {
        var sql = SqlScripts.ReadSqlScript("Shared/Queries/Api/AuthenticationRepository/GetActiveUsersForUser");
        await using var connection = await connections.OpenConnection(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<UserRow>(new CommandDefinition(
            sql,
            new { UserId = userId },
            cancellationToken: cancellationToken));
        return row is null ? null : new CurrentUserDto(
            row.Id,
            row.DisplayName,
            row.MaskedEmail,
            SplitAggregate(row.Roles),
            SplitAggregate(row.Permissions));
    }

    public async Task<PasswordState?> GetPasswordState(Guid user, CancellationToken ct)
    {
        await using var c = await connections.OpenConnection(ct);
        return await c.QuerySingleOrDefaultAsync<PasswordState>(new CommandDefinition(
            """
            SELECT
                "users"."passwordHash" AS PasswordHash,
                "users"."accountStatus" AS Status
            FROM "identity"."users" AS "users"
            WHERE "users"."publicId" = @Id
            """,
            new { Id = user },
            cancellationToken: ct));
    }

    public async Task ChangePassword(Guid user, string expectedHash, string newHash, CancellationToken ct)
    {
        await using var c = await connections.OpenConnection(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await LockOperations(c, tx, ct);
        var changed = await c.ExecuteAsync(new CommandDefinition(
            """
            UPDATE "identity"."users"
            SET
                "passwordHash" = @Hash,
                "updatedAt" = @Now
            WHERE "users"."publicId" = @Id
            AND "users"."passwordHash" = @Expected
            AND "users"."accountStatus" = 'Active'
            """,
            new
            {
                Id = user,
                Hash = newHash,
                Expected = expectedHash,
                Now = DateTimeOffset.UtcNow
            },
            tx,
            cancellationToken: ct));
        if (changed != 1)
        {
            throw new PortalConflictException("Your security details changed. Sign in again before updating your password.");
        }

        await c.ExecuteAsync(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/AuthenticationRepository/RevokeUserSessionsAndPendingChallenges"),
            new { Id = user, Now = DateTimeOffset.UtcNow },
            tx,
            cancellationToken: ct));
        await AuditOperation(c, tx, user, "PasswordChanged", user, new { outcome = "Success", sessions = "AllRevoked" }, ct);
        await tx.CommitAsync(ct);
    }

    private class UserRow
    {
        public Guid Id { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public string MaskedEmail { get; set; } = string.Empty;
        public string Roles { get; set; } = string.Empty;
        public string Permissions { get; set; } = string.Empty;

        public UserRow()
        {
        }

        public UserRow(Guid Id, string DisplayName, string MaskedEmail, string Roles, string Permissions)
        {
            this.Id = Id;
            this.DisplayName = DisplayName;
            this.MaskedEmail = MaskedEmail;
            this.Roles = Roles;
            this.Permissions = Permissions;
        }
    }

    private class LoginRow
    {
        public Guid UserId { get; set; }
        public string LoginIdentifier { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public string AccountStatus { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Roles { get; set; } = string.Empty;
        public string Permissions { get; set; } = string.Empty;

        public LoginRow()
        {
        }

        public LoginRow(
            Guid UserId,
            string LoginIdentifier,
            string PasswordHash,
            string AccountStatus,
            string DisplayName,
            string Roles,
            string Permissions)
        {
            this.UserId = UserId;
            this.LoginIdentifier = LoginIdentifier;
            this.PasswordHash = PasswordHash;
            this.AccountStatus = AccountStatus;
            this.DisplayName = DisplayName;
            this.Roles = Roles;
            this.Permissions = Permissions;
        }
    }
}
