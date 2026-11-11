using DisputePortal.Api.Configuration;
using Dapper;
using DisputePortal.Api.Models.Exceptions;
using DisputePortal.Api.Models.Responses;
using DisputePortal.Api.Interfaces.Data;
using DisputePortal.Api.Interfaces.Repositories;
using DisputePortal.Api.Interfaces.Security;
using DisputePortal.Platform.Infrastructure.Sql;

namespace DisputePortal.Api.Repository;

public class ContactVerificationRepository(
    IDatabaseConnectionFactory factory,
    IDatabaseDialect databaseDialect,
    ISecretProtector secretProtector,
    IOtpHasher otpHasher,
    ApiRuntimeOptions runtime) : RepositoryBase(factory, databaseDialect), IContactVerificationRepository
{
    public async Task<ContactChallengeDto> ReserveContactChallenge(
        Guid user,
        string field,
        string destination,
        string channel,
        string code,
        CancellationToken ct)
    {
        await using var c = await connections.OpenConnection(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await LockOperations(c, tx, ct);
        var now = DateTimeOffset.UtcNow;
        var recent = await c.ExecuteScalarAsync<int>(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/ContactVerificationRepository/CountContactVerificationChallengesForUser"),
            new { User = user, Since = now.AddSeconds(-runtime.VerificationResendDelaySeconds) },
            tx,
            cancellationToken: ct));
        if (recent > 0)
        {
            throw new PortalConflictException($"Please wait {runtime.VerificationResendDelaySeconds} seconds before requesting another code.");
        }

        if (field == "email"
            && await c.ExecuteScalarAsync<int>(new CommandDefinition(
            SqlScripts.RenderSqlScript(
            "Shared/Queries/Api/ContactVerificationRepository/CountConflictingVerifiedContactsBeforeChange",
            ("TrueValue", dialect.TrueLiteral)),
            new { User = user, Email = destination.ToUpperInvariant() },
            tx,
            cancellationToken: ct)) > 0)
        {
            throw new PortalConflictException("That email cannot be used.");
        }

        var id = Guid.NewGuid();
        var expires = now.AddMinutes(runtime.VerificationCodeLifetimeMinutes);
        // Resending invalidates all earlier codes for this user, regardless of channel.
        await c.ExecuteAsync(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/ContactVerificationRepository/MarkContactVerificationChallengesFailedForUser"),
            new
            {
                Id = id,
                User = user,
                Field = field,
                Channel = channel,
                Destination = secretProtector.Protect(destination),
                Hash = otpHasher.Hash(id, code),
                Now = now,
                Expires = expires
            },
            tx,
            cancellationToken: ct));
        await tx.CommitAsync(ct);
        var masked = field == "email" ? destination[..1] + "••••@" + destination.Split('@')[1] : "••••" + destination[^4..];
        return new ContactChallengeDto(id, masked, expires);
    }

    public async Task SetContactChallengeDelivery(Guid user, Guid challenge, bool delivered, CancellationToken ct)
    {
        await using var c = await connections.OpenConnection(ct);
        await c.ExecuteAsync(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/ContactVerificationRepository/UpdateContactVerificationChallengesForUser"),
            new
            {
                Id = challenge,
                User = user,
                State = delivered ? "Ready" : "Failed"
            },
            cancellationToken: ct));
    }

    public async Task CompleteContactChange(Guid user, Guid challenge, string code, CancellationToken ct)
    {
        await using var c = await connections.OpenConnection(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await LockOperations(c, tx, ct);
        var row = await c.QuerySingleOrDefaultAsync<ContactChallengeRow>(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/ContactVerificationRepository/GetContactVerificationChallengeForUser"),
            new { Id = challenge, User = user },
            tx,
            cancellationToken: ct));
        var now = DateTimeOffset.UtcNow;
        if (row is null || row.State != "Ready" || row.Attempts >= 5 || row.ExpiresAt <= now)
        {
            throw new PortalValidationException("Request a new verification code to continue.");
        }

        if (!otpHasher.Matches(challenge, code, row.CodeHash))
        {
            await c.ExecuteAsync(new CommandDefinition(
                """
                UPDATE "identity"."contactVerificationChallenges"
                SET
                    "attempts" = "contactVerificationChallenges"."attempts" + 1
                WHERE "contactVerificationChallenges"."publicId" = @Id
                """,
                new { Id = challenge },
                tx,
                cancellationToken: ct));
            // Commit failed attempts before returning the error; a rollback must not reset the counter.
            await tx.CommitAsync(ct);
            throw new PortalValidationException("Check the six-digit code. You can try up to five times before requesting a new code.");
        }

        var destination = secretProtector.Unprotect(row.ProtectedDestination);
        if (row.Purpose == "email")
        {
            if (await c.ExecuteScalarAsync<int>(new CommandDefinition(
                SqlScripts.RenderSqlScript(
                "Shared/Queries/Api/ContactVerificationRepository/CountConflictingVerifiedContactsBeforeChange",
                ("TrueValue", dialect.TrueLiteral)),
                new { User = user, Email = destination.ToUpperInvariant() },
                tx,
                cancellationToken: ct)) > 0)
            {
                throw new PortalConflictException("That email cannot be used.");
            }

            await c.ExecuteAsync(new CommandDefinition(
                SqlScripts.RenderSqlScript(
                "Shared/Queries/Api/ContactVerificationRepository/ApplyVerifiedEmailToUserAndProfile",
                ("TrueValue", dialect.TrueLiteral),
                ("RelatedTrueValue", dialect.TrueLiteral)),
                new
                {
                    User = user,
                    Destination = destination,
                    Normalized = destination.ToUpperInvariant(),
                    Now = now,
                    Masked = destination[..1] + "••••@" + destination.Split('@')[1]
                },
                tx,
                cancellationToken: ct));
        }
        else
        {
            if (await c.ExecuteScalarAsync<int>(new CommandDefinition(
                SqlScripts.RenderSqlScript(
                "Shared/Queries/Api/ContactVerificationRepository/CountUsersForUserByUserAndDestination",
                ("TrueValue", dialect.TrueLiteral),
                ("RelatedTrueValue", dialect.TrueLiteral)),
                new { User = user, Destination = destination },
                tx,
                cancellationToken: ct)) > 0)
            {
                throw new PortalConflictException("That mobile number cannot be used.");
            }

            var login = await c.ExecuteScalarAsync<string>(new CommandDefinition(
                """
                SELECT
                    "users"."loginIdentifier" AS "login_identifier"
                FROM "identity"."users" AS "users"
                WHERE "users"."publicId" = @User
                """,
                new { User = user },
                tx,
                cancellationToken: ct));
            if (login?.StartsWith('+') == true)
            {
                await c.ExecuteAsync(new CommandDefinition(
                    """
                    UPDATE "identity"."users"
                    SET
                        "loginIdentifier" = @Destination,
                        "normalizedLoginIdentifier" = @Destination,
                        "updatedAt" = @Now
                    WHERE "users"."publicId" = @User
                    """,
                    new
                    {
                        User = user,
                        Destination = destination,
                        Now = now
                    },
                    tx,
                    cancellationToken: ct));
            }

            await c.ExecuteAsync(new CommandDefinition(
                SqlScripts.RenderSqlScript(
                "Shared/Queries/Api/ContactVerificationRepository/UpdateCustomerProfilesForUser",
                ("TrueValue", dialect.TrueLiteral)),
                new
                {
                    User = user,
                    Destination = destination,
                    Channel = row.Channel,
                    Now = now,
                    Masked = "••••" + destination[^4..]
                },
                tx,
                cancellationToken: ct));
        }

        await c.ExecuteAsync(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/ContactVerificationRepository/MarkIdentityChallengesFailedForUser"),
            new
            {
                Id = challenge,
                User = user,
                Now = now
            },
            tx,
            cancellationToken: ct));
        await AuditOperation(c, tx, user, "VerifiedContactChanged", user, new { field = row.Purpose }, ct);
        await tx.CommitAsync(ct);
    }

    private class ContactChallengeRow
    {
        public string Purpose { get; set; } = "";
        public string Channel { get; set; } = "";
        public string ProtectedDestination { get; set; } = "";
        public string CodeHash { get; set; } = "";
        public string State { get; set; } = "";
        public int Attempts { get; set; }
        public DateTimeOffset ExpiresAt { get; set; }
    }
}
