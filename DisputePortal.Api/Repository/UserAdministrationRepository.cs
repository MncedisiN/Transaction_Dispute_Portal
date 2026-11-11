using Dapper;
using DisputePortal.Api.Models.Exceptions;
using DisputePortal.Api.Models.Requests;
using DisputePortal.Api.Models.Responses;
using DisputePortal.Api.Models.Domain;
using DisputePortal.Api.Interfaces.Data;
using DisputePortal.Api.Interfaces.Repositories;
using DisputePortal.Platform.Infrastructure.Sql;

namespace DisputePortal.Api.Repository;

public class UserAdministrationRepository(IDatabaseConnectionFactory factory, IDatabaseDialect databaseDialect) : RepositoryBase(
    factory,
    databaseDialect), IUserAdministrationRepository
{
    public async Task<IReadOnlyList<StaffUserDto>> GetStaff(Guid actor, bool administration, CancellationToken ct)
    {
        await using var c = await connections.OpenConnection(ct);
        if (administration)
        {
            await RequireSupervisor(c, null, actor, ct, true);
        }
        else if (!(await Roles(c, null, actor, ct)).Any(r => CaseServicePolicy.IsSupervisor(r) || CaseServicePolicy.IsSpecialist(r) || r is "audit_reader" or "service_clerk"))
        {
            throw new PortalForbiddenException();
        }

        var roleFilter = administration ? "" : SqlScripts.RenderSqlScript(
            "Shared/Queries/Api/UserAdministrationRepository/FilterUsersByAllowedRoleCodes",
            ("AssignableRolesSql", AssignableRolesSql));
        var sql = SqlScripts.RenderSqlScript(
            "Shared/Queries/Api/UserAdministrationRepository/GetUserAdministrationPage",
            ("StaffSelect", StaffSelect),
            ("RoleFilter", roleFilter));
        return (await c.QueryAsync<StaffUserDto>(new CommandDefinition(sql, cancellationToken: ct))).AsList();
    }

    public async Task<Guid> CreateUser(Guid actor, SaveUserRequest request, string passwordHash, CancellationToken ct)
    {
        var name = ValidatePersonalName(request.FirstName, request.LastName, request.MiddleName);
        if (!AllowedRoles.Contains(request.Role))
        {
            throw new PortalValidationException("Select a valid role.");
        }

        await using var c = await connections.OpenConnection(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await LockOperations(c, tx, ct);
        await RequireSupervisor(c, tx, actor, ct, true);
        var email = request.Email.Trim();
        if (await c.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT
                COUNT(*)
            FROM "identity"."users" AS "users"
            WHERE "users"."normalizedLoginIdentifier" = @Email
            """,
            new { Email = email.ToUpperInvariant() },
            tx,
            cancellationToken: ct)) > 0)
        {
            throw new PortalConflictException("An account with this email already exists.");
        }

        var id = Guid.NewGuid();
        await c.ExecuteAsync(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/UserAdministrationRepository/InsertUser"),
            new
            {
                Id = id,
                Email = email,
                Normalized = email.ToUpperInvariant(),
                Hash = passwordHash,
                Name = name,
                FirstName = request.FirstName!.Trim(),
                MiddleName = string.IsNullOrWhiteSpace(request.MiddleName) ? null : request.MiddleName.Trim(),
                LastName = request.LastName!.Trim(),
                Masked = email[..1] + "••••@" + email.Split('@')[1],
                request.Role
            },
            tx,
            cancellationToken: ct));
        await AuditOperation(c, tx, actor, "UserCreated", id, new { request.Role }, ct);
        await tx.CommitAsync(ct);
        return id;
    }

    private static readonly string[] AllowedRoles = ["customer", "service_clerk", "dispute_agent", "fraud_agent", "card_specialist", "debit_order_specialist", "payments_specialist", "operations_manager", "super_admin", "audit_reader"];

    public async Task UpdateUser(Guid actor, Guid id, UpdateUserRequest request, CancellationToken ct)
    {
        if (!AllowedRoles.Contains(request.Role) || request.Status is not ("Active" or "Disabled" or "Locked"))
        {
            throw new PortalValidationException("Select a valid role\nand account status.");
        }

        if (actor == id)
        {
            throw new PortalConflictException("Ask another Super Admin to change your own account.");
        }

        await using var c = await connections.OpenConnection(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await LockOperations(c, tx, ct);
        await RequireSupervisor(c, tx, actor, ct, true);
        var target = await c.QueryFirstOrDefaultAsync<StaffUserDto>(new CommandDefinition(
            SqlScripts.RenderSqlScript(
            "Shared/Queries/Api/UserAdministrationRepository/GetUserAdministrationById",
            ("StaffSelect", StaffSelect)),
            new { Id = id },
            tx,
            cancellationToken: ct));
        if (target is null)
        {
            throw new PortalNotFoundException();
        }

        var changingName = request.FirstName is not null || request.LastName is not null || request.MiddleName is not null;
        var name = changingName ? ValidatePersonalName(request.FirstName, request.LastName, request.MiddleName) : null;
        if (changingName
            && await c.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT
                COUNT(*)
            FROM "identity"."users" AS "users"
            WHERE "users"."publicId" = @Id
            AND "users"."deletedAt" IS NOT NULL
            """,
            new { Id = id },
            tx,
            cancellationToken: ct)) > 0)
        {
            throw new PortalConflictException("A closed account's erased profile cannot be edited.");
        }

        if (request.Status == "Active"
            && await c.ExecuteScalarAsync<int>(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/UserAdministrationRepository/CountCompletedAccountDeletionRequests"),
            new { Id = id },
            tx,
            cancellationToken: ct)) > 0)
        {
            throw new PortalConflictException("Completed account deletions cannot be reactivated.");
        }

        if (target.Role == "super_admin"
            && (request.Role != "super_admin"
            || request.Status != "Active")
            && await c.ExecuteScalarAsync<int>(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/UserAdministrationRepository/CountActiveUsers"),
            transaction: tx,
            cancellationToken: ct)) <= 1)
        {
            throw new PortalConflictException("Keep at least one active Super Admin.");
        }

        if (target.ActiveCases > 0 && (request.Role != target.Role || request.Status != "Active"))
        {
            throw new PortalConflictException("Reassign this user's open cases before changing their role or disabling them.");
        }

        if (request.Capacity < target.ActiveCases)
        {
            throw new PortalConflictException("Capacity cannot be below the current assigned workload.");
        }

        if (changingName)
        {
            await c.ExecuteAsync(new CommandDefinition(
                SqlScripts.ReadSqlScript("Shared/Queries/Api/UserAdministrationRepository/UpdateCustomerProfiles"),
                new
                {
                    Id = id,
                    Name = name,
                    FirstName = request.FirstName!.Trim(),
                    MiddleName = string.IsNullOrWhiteSpace(request.MiddleName) ? null : request.MiddleName.Trim(),
                    LastName = request.LastName!.Trim(),
                    Now = DateTimeOffset.UtcNow
                },
                tx,
                cancellationToken: ct));
            await AuditOperation(c, tx, actor, "UserNameChanged", id, new { }, ct);
        }

        await c.ExecuteAsync(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/UserAdministrationRepository/UpdateUsers"),
            new
            {
                Id = id,
                request.Role,
                request.Status,
                request.Capacity,
                Now = DateTimeOffset.UtcNow
            },
            tx,
            cancellationToken: ct));
        await AuditOperation(
            c,
            tx,
            actor,
            "UserAccessChanged",
            id,
            new
            {
                previousRole = target.Role,
                previousStatus = target.Status,
                request.Role,
                request.Status,
                request.Capacity
            },
            ct);
        await tx.CommitAsync(ct);
    }

    internal static string ValidatePersonalName(string? firstName, string? lastName, string? middleName)
    {
        static bool Valid(string? value, bool required) => string.IsNullOrWhiteSpace(value) ? !required : value.Trim().Length <= 100
            && value.Trim().Any(char.IsLetter)
            && value.All(c => !char.IsControl(c)
            && (char.IsLetter(c)
            || char.IsWhiteSpace(c)
            || c is '-' or '\'' or '’'
            || char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.NonSpacingMark));
        if (!Valid(firstName, true) || !Valid(lastName, true) || !Valid(middleName, false))
        {
            throw new PortalValidationException("Enter the person's first name and surname, using letters, spaces, apostrophes or hyphens.");
        }

        var name = firstName!.Trim() + " " + lastName!.Trim();
        if (name.Length > 200)
        {
            throw new PortalValidationException("Keep the combined first name and surname within 200 characters.");
        }

        return name;
    }
}
