using DisputePortal.Api.Configuration;
using Dapper;
using DisputePortal.Api.Models.Exceptions;
using DisputePortal.Api.Models.Responses;
using System.Text.Json;
using System.Data.Common;
using DisputePortal.Api.Models.Internal;
using DisputePortal.Api.Models.Domain;
using DisputePortal.Api.Interfaces.Data;
using DisputePortal.Api.Interfaces.Repositories;
using DisputePortal.Api.Interfaces.Security;
using DisputePortal.Platform.Infrastructure.Sql;

namespace DisputePortal.Api.Repository;

public class IdentityVerificationRepository(
    IDatabaseConnectionFactory factory,
    IDatabaseDialect databaseDialect,
    ISecretProtector secretProtector,
    IOtpHasher otpHasher,
    ApiRuntimeOptions runtime) : RepositoryBase(factory, databaseDialect), IIdentityVerificationRepository
{
    public async Task<VerifiedIdentity?> FindIdentity(string contact, CancellationToken ct)
    {
        await using var c = await connections.OpenConnection(ct);
        return await c.QuerySingleOrDefaultAsync<VerifiedIdentity>(new CommandDefinition(
            SqlScripts.RenderSqlScript(
            "Shared/Queries/Api/IdentityVerificationRepository/GetActiveUser",
            ("TrueValue", dialect.TrueLiteral)),
            new { Contact = contact.Trim().ToUpperInvariant() },
            cancellationToken: ct));
    }

    public async Task<bool> RegistrationContactExists(string contact, CancellationToken ct)
    {
        await using var c = await connections.OpenConnection(ct);
        return await RegistrationContactExists(c, null, contact, ct);
    }

    private static async Task<bool> RegistrationContactExists(DbConnection c, DbTransaction? tx, string contact, CancellationToken ct)
    {
        var normalized = contact.Trim().ToUpperInvariant();
        string? phone = null, digits = null, international = null, local = null;
        if (ContactNumber.TryNormalize(contact, out var financialCommands))
        {
            normalized = financialCommands;
            phone = financialCommands;
            digits = financialCommands[1..];
            international = "00" + digits;
            if (financialCommands.StartsWith("+27", StringComparison.Ordinal) && financialCommands.Length == 12)
            {
                local = "0" + financialCommands[3..];
            }
        }

        // Registration reserves contacts across every profile, including shared staff contacts
        // that cannot be used for contact-based sign-in. COUNT never assumes a unique owner.
        var sql = SqlScripts.ReadSqlScript("Shared/Queries/Api/IdentityVerificationRepository/CountUsers");
        var count = await c.ExecuteScalarAsync<int>(new CommandDefinition(
            sql,
            new
            {
                Contact = normalized,
                Phone = phone,
                Digits = digits,
                International = international,
                Local = local
            },
            tx,
            cancellationToken: ct));
        return count > 0;
    }

    public async Task<IdentityChallengeDto> CreateIdentityChallenge(
        Guid? user,
        string purpose,
        string channel,
        string destination,
        string payload,
        string code,
        CancellationToken ct)
    {
        if (purpose is not ("Registration" or "Login" or "Recovery"))
        {
            throw new ArgumentException("Unknown verification purpose.");
        }

        if (channel is not ("email" or "sms" or "whatsapp"))
        {
            throw new PortalValidationException("Choose email, SMS or WhatsApp verification.");
        }

        await using var c = await connections.OpenConnection(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await LockOperations(c, tx, ct);
        if (purpose == "Registration" && await RegistrationContactExists(c, tx, destination, ct))
        {
            throw new PortalConflictException("This contact is already registered. Sign in or use password recovery.");
        }

        var now = DateTimeOffset.UtcNow;
        var hash = otpHasher.Hash(Guid.Empty, purpose + ":" + destination.Trim().ToUpperInvariant());
        if (await c.ExecuteScalarAsync<int>(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/IdentityVerificationRepository/CountIdentityChallenges"),
            new
            {
                Hash = hash,
                Purpose = purpose,
                Since = now.AddSeconds(-runtime.VerificationResendDelaySeconds)
            },
            tx,
            cancellationToken: ct)) > 0)
        {
            throw new PortalConflictException($"Please wait {runtime.VerificationResendDelaySeconds} seconds before requesting another code.");
        }

        var id = Guid.NewGuid();
        var expires = now.AddMinutes(runtime.VerificationCodeLifetimeMinutes);
        await c.ExecuteAsync(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/IdentityVerificationRepository/MarkIdentityChallengesFailedForUser"),
            new
            {
                Id = id,
                User = user,
                Purpose = purpose,
                Channel = channel,
                Hash = hash,
                Payload = secretProtector.Protect(payload),
                CodeHash = otpHasher.Hash(id, code),
                Now = now,
                Expires = expires
            },
            tx,
            cancellationToken: ct));
        await tx.CommitAsync(ct);
        var masked = destination.Contains('@') ? destination[..1] + "••••@" + destination.Split('@')[1] : "••••" + destination[^Math.Min(
            4,
            destination.Length)..];
        return new(id, channel, masked, expires);
    }

    public async Task MarkIdentityChallengeReady(Guid challenge, CancellationToken ct)
    {
        await using var c = await connections.OpenConnection(ct);
        await c.ExecuteAsync(new CommandDefinition(
            """
            UPDATE "identity"."identityChallenges"
            SET
                "state" = 'Ready'
            WHERE "identityChallenges"."publicId" = @Id
            AND "identityChallenges"."state" = 'Pending'
            """,
            new { Id = challenge },
            cancellationToken: ct));
    }

    private async Task<IdentityChallengeRow> CheckIdentityChallenge(
        DbConnection c,
        DbTransaction tx,
        Guid id,
        string purpose,
        string code,
        CancellationToken ct)
    {
        var row = await c.QuerySingleOrDefaultAsync<IdentityChallengeRow>(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/IdentityVerificationRepository/GetIdentityChallenge"),
            new { Id = id, Purpose = purpose },
            tx,
            cancellationToken: ct));
        if (row is null
            || row.State != "Ready"
            || row.Channel is not ("email" or "sms" or "whatsapp")
            || row.Attempts >= 5
            || row.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            throw new PortalValidationException("This verification has expired or is unavailable. Start again to request a new code.");
        }

        var matches = otpHasher.Matches(id, code, row.CodeHash);
        if (!matches)
        {
            await c.ExecuteAsync(new CommandDefinition(
                """
                UPDATE "identity"."identityChallenges"
                SET
                    "attempts" = "identityChallenges"."attempts" + 1
                WHERE "identityChallenges"."publicId" = @Id
                """,
                new { Id = id },
                tx,
                cancellationToken: ct));
            await tx.CommitAsync(ct);
            throw new PortalValidationException("Check your six-digit code and try again. Request a new code after five unsuccessful attempts.");
        }

        if (row.UserId is { } owner
            && await c.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT
                COUNT(*)
            FROM "identity"."users" AS "users"
            WHERE "users"."publicId" = @User
            AND "users"."accountStatus" = 'Active'
            """,
            new { User = owner },
            tx,
            cancellationToken: ct)) != 1)
        {
            throw new PortalValidationException("Start verification again to continue.");
        }

        return row;
    }

    private Task ConsumeIdentity(DbConnection c, DbTransaction tx, Guid id, CancellationToken ct) => c.ExecuteAsync(new CommandDefinition(
        """
        UPDATE "identity"."identityChallenges"
        SET
            "state" = 'Used',
            "protectedPayload" = ''
        WHERE "identityChallenges"."publicId" = @Id
        """,
        new { Id = id },
        tx,
        cancellationToken: ct));

    public async Task<Guid> CompleteRegistration(Guid challenge, string code, CancellationToken ct)
    {
        await using var c = await connections.OpenConnection(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await LockOperations(c, tx, ct);
        var row = await CheckIdentityChallenge(c, tx, challenge, "Registration", code, ct);
        var registration = JsonSerializer.Deserialize<RegistrationPayload>(secretProtector.Unprotect(row.ProtectedPayload)) ?? throw new PortalValidationException("Start registration again.");
        var id = await SaveRegisteredCustomer(c, tx, registration, true, ct);
        await ConsumeIdentity(c, tx, challenge, ct);
        await tx.CommitAsync(ct);
        return id;
    }

    public async Task<Guid> RegisterWithoutContactVerification(RegistrationPayload registration, CancellationToken ct)
    {
        await using var c = await connections.OpenConnection(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await LockOperations(c, tx, ct);
        var id = await SaveRegisteredCustomer(c, tx, registration, false, ct);
        await tx.CommitAsync(ct);
        return id;
    }

    private async Task<Guid> SaveRegisteredCustomer(
        DbConnection c,
        DbTransaction tx,
        RegistrationPayload registration,
        bool verifiedContact,
        CancellationToken ct)
    {
        if (await RegistrationContactExists(c, tx, registration.Contact, ct)
            || registration.Details is { } profile
            && (await RegistrationContactExists(c, tx, profile.Email, ct)
            || await RegistrationContactExists(c, tx, profile.Phone, ct)))
        {
            throw new PortalConflictException("This contact is already registered. Sign in or use password recovery.");
        }

        var id = Guid.NewGuid();
        var email = registration.Details?.Email ?? (registration.Channel == "email" ? registration.Contact : null);
        var phone = registration.Details?.Phone ?? (registration.Channel != "email" ? registration.Contact : null);
        await c.ExecuteAsync(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/IdentityVerificationRepository/InsertUser"),
            new
            {
                Id = id,
                Contact = registration.Contact,
                Normalized = registration.Contact.ToUpperInvariant(),
                registration.PasswordHash,
                Name = registration.DisplayName.Trim(),
                registration.FirstName,
                registration.LastName,
                MaskedEmail = email is null ? "Not added" : email[..1] + "••••@" + email.Split('@')[1],
                MaskedPhone = phone is null ? "Not added" : "••••" + phone[^4..],
                Email = email,
                Phone = phone,
                EmailVerified = verifiedContact && registration.Channel == "email",
                PhoneVerified = verifiedContact && registration.Channel != "email",
                Channel = registration.Channel
            },
            tx,
            cancellationToken: ct));
        if (registration.Details is { } details)
        {
            await c.ExecuteAsync(new CommandDefinition(
                SqlScripts.ReadSqlScript("Shared/Queries/Api/IdentityVerificationRepository/UpdateCustomerProfiles"),
                new
                {
                    Id = id,
                    details.CountryCode,
                    details.PhoneCountry,
                    details.DocumentType,
                    details.DocumentIssuer,
                    details.AccountCurrency,
                    BirthDate = details.DateOfBirth?.ToDateTime(TimeOnly.MinValue),
                    Identity = secretProtector.Protect(details.DocumentNumber),
                    LastFour = details.DocumentNumber[^4..],
                    Address = details.Address is null ? null : secretProtector.Protect(JsonSerializer.Serialize(details.Address))
                },
                tx,
                cancellationToken: ct));
            await DisputePortal.Platform.Infrastructure.ServiceDatabase.SaveDomainEventToOutbox(
                c,
                tx,
                DisputePortal.Platform.Contracts.DomainEvent.CreateDomainEvent(
                    "identity",
                    DisputePortal.Platform.Contracts.EventTypes.UserRegistered,
                    new DisputePortal.Platform.Contracts.UserRegisteredEvent
                    {
                        UserId = id,
                        CountryCode = details.CountryCode,
                        AccountCurrency = details.AccountCurrency
                    },
                    id),
                ct);
        }

        await AuditOperation(c, tx, id, "CustomerRegistered", id, new { channel = registration.Channel, verifiedContact }, ct);
        return id;
    }

    public async Task<Guid> CompleteLogin(Guid challenge, string code, CancellationToken ct)
    {
        await using var c = await connections.OpenConnection(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await LockOperations(c, tx, ct);
        var row = await CheckIdentityChallenge(c, tx, challenge, "Login", code, ct);
        var user = row.UserId ?? throw new PortalValidationException("Start sign-in again.");
        await ConsumeIdentity(c, tx, challenge, ct);
        await tx.CommitAsync(ct);
        return user;
    }

    public async Task CompletePasswordReset(
        Guid challenge,
        string code,
        string passwordHash,
        CancellationToken ct,
        Func<string, bool>? matchesCurrentPassword = null)
    {
        await using var c = await connections.OpenConnection(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await LockOperations(c, tx, ct);
        var row = await CheckIdentityChallenge(c, tx, challenge, "Recovery", code, ct);
        var user = row.UserId ?? throw new PortalValidationException("Start recovery again.");
        var previousHash = await c.ExecuteScalarAsync<string>(new CommandDefinition(
            """
            SELECT
                "users"."passwordHash" AS "password_hash"
            FROM "identity"."users" AS "users"
            WHERE "users"."publicId" = @Id
            """,
            new { Id = user },
            tx,
            cancellationToken: ct));
        if (matchesCurrentPassword?.Invoke(previousHash!) == true)
        {
            throw new PortalValidationException("Choose a different password from your current password.");
        }

        await c.ExecuteAsync(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/IdentityVerificationRepository/UpdatePasswordAndRevokeUserAccess"),
            new
            {
                User = user,
                Hash = passwordHash,
                Now = DateTimeOffset.UtcNow
            },
            tx,
            cancellationToken: ct));
        await ConsumeIdentity(c, tx, challenge, ct);
        await AuditOperation(c, tx, user, "PasswordReset", user, new { }, ct);
        await tx.CommitAsync(ct);
    }

    private class IdentityChallengeRow
    {
        public Guid? UserId { get; set; }
        public string Channel { get; set; } = "";
        public string ProtectedPayload { get; set; } = "";
        public string CodeHash { get; set; } = "";
        public string State { get; set; } = "";
        public int Attempts { get; set; }
        public DateTimeOffset ExpiresAt { get; set; }
    }
}
