using System.Net.Mail;
using Dapper;
using DisputePortal.Api.Models;
using DisputePortal.Api.Models.Domain;
using Microsoft.AspNetCore.Identity;
using DisputePortal.Api.Interfaces.Data;
using DisputePortal.Platform.Infrastructure.Sql;

namespace DisputePortal.Api.Repository; 
public class DevelopmentUserInitializer(
    IDatabaseConnectionFactory factory,
    IDatabaseDialect databaseDialect,
    IPasswordHasher<PortalPasswordUser> passwords) : RepositoryBase(factory, databaseDialect)
{
    public async Task<int> InitializeConfiguredDevelopmentUsers(
        IHostEnvironment environment,
        IConfiguration configuration,
        CancellationToken ct)
    {
        if (!configuration.GetValue("DevelopmentSeed:Enabled", false))
        {
            return 0;
        }

        if (!environment.IsDevelopment())
        {
            throw new InvalidOperationException("Development users cannot be initialized outside Development.");
        }

        var users = configuration.GetSection("DevelopmentSeed:Users").Get<ConfiguredUser[]>() ?? [];
        if (users.Length is < 1 or > 20)
        {
            throw new InvalidOperationException("Configure between one and twenty development users through private settings.");
        }

        var identifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var user in users)
        {
            user.Email = user.Email?.Trim() ?? "";
            if (user.Email.Length > 320
                || !MailAddress.TryCreate(user.Email, out var address)
                || address.Address != user.Email
                || !identifiers.Add(user.Email))
            {
                throw new InvalidOperationException("Development users require distinct valid email addresses.");
            }

            UserAdministrationRepository.ValidatePersonalName(user.FirstName, user.LastName, user.MiddleName);
            if (string.IsNullOrEmpty(user.PasswordHash) && !PasswordPolicy.IsStrong(user.Password))
            {
                throw new InvalidOperationException("Development user passwords must meet the application password policy.");
            }

            if (!string.IsNullOrEmpty(user.PasswordHash))
            {
                var hashBytes = Convert.FromBase64String(user.PasswordHash);
                if (hashBytes.Length != 61 || hashBytes[0] != 1
                    || System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(hashBytes.AsSpan(1, 4)) != 2
                    || System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(hashBytes.AsSpan(5, 4)) is < 100000 or > 1000000
                    || System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(hashBytes.AsSpan(9, 4)) != 16)
                {
                    throw new InvalidOperationException("Configured password hashes must use the supported Identity format.");
                }
            }

            if (user.Roles is not { Length: > 0 and <= 10 }
                || user.Roles.Any(role => string.IsNullOrWhiteSpace(role)
                || role.Length > 64)
                || user.Roles.Distinct(StringComparer.Ordinal).Count() != user.Roles.Length)
            {
                throw new InvalidOperationException("Development users require distinct configured role codes.");
            }
        }

        await using var connection = await connections.OpenConnection(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await LockOperations(connection, transaction, ct);

        var roles = users.SelectMany(user => user.Roles).Distinct(StringComparer.Ordinal).ToArray();
        var existingRoles = (await connection.QueryAsync<string>(new CommandDefinition(
            SqlScripts.RenderSqlScript(
            "Shared/Queries/Api/DevelopmentUserInitializer/GetRoles",
            ("MembershipPredicate", dialect.InListExpression("code", "@Roles"))),
            new { Roles = roles },
            transaction,
            cancellationToken: ct))).ToHashSet(StringComparer.Ordinal);
        if (roles.Any(role => !existingRoles.Contains(role)))
        {
            throw new InvalidOperationException("A configured development role is not present in the reference catalogue.");
        }

        var created = 0;
        foreach (var user in users)
        {
            var normalized = user.Email.ToUpperInvariant();
            if (await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                SqlScripts.ReadSqlScript("Shared/Queries/Api/DevelopmentUserInitializer/CountUsers"),
                new { Normalized = normalized },
                transaction,
                cancellationToken: ct)) > 0)
            {
                continue;
            }

            var id = Guid.NewGuid();
            var hash = string.IsNullOrEmpty(user.PasswordHash) ? passwords.HashPassword(new PortalPasswordUser(id), user.Password) : user.PasswordHash;
            var internalUserId = await connection.QuerySingleAsync<long>(new CommandDefinition(
                SqlScripts.ReadSqlScript("Shared/Queries/Api/DevelopmentUserInitializer/InsertUser"),
                new
                {
                    Id = id,
                    user.Email,
                    Normalized = normalized,
                    Hash = hash
                },
                transaction,
                cancellationToken: ct));
            await connection.ExecuteAsync(new CommandDefinition(
                SqlScripts.RenderSqlScript(
                "Shared/Queries/Api/DevelopmentUserInitializer/InsertCustomerProfile",
                ("MembershipPredicate", dialect.InListExpression("code", "@Roles"))),
                new
                {
                    UserId = internalUserId,
                    user.Email,
                    Name = UserAdministrationRepository.ValidatePersonalName(user.FirstName, user.LastName, user.MiddleName),
                    FirstName = user.FirstName.Trim(),
                    MiddleName = string.IsNullOrWhiteSpace(user.MiddleName) ? null : user.MiddleName.Trim(),
                    LastName = user.LastName.Trim(),
                    Masked = user.Email[..1] + "••••@" + user.Email.Split('@')[1],
                    user.Roles
                },
                transaction,
                cancellationToken: ct));
            if (user.Roles.Any(role => role != "customer"))
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO support."staffCapacity"(
                        "userId"
                    )
                    VALUES (
                        @UserId
                    )
                    """,
                    new { UserId = internalUserId },
                    transaction,
                    cancellationToken: ct));
            }

            await AuditOperation(connection, transaction, null, "DevelopmentUserCreated", id, new { }, ct);
            created++;
        }

        await transaction.CommitAsync(ct);
        return created;
    }

    public class ConfiguredUser
    {
        public string Email { get; set; } = "";
        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";
        public string? MiddleName { get; set; }
        public string Password { get; set; } = "";
        public string PasswordHash { get; set; } = "";
        public string[] Roles { get; set; } = [];
    }
}
