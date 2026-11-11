using Dapper;
using DisputePortal.Api.Models.Exceptions;
using DisputePortal.Api.Models.Responses;
using System.Text.Json;
using System.Data.Common;
using DisputePortal.Api.Models.Domain;
using DisputePortal.Api.Interfaces.Data;
using DisputePortal.Platform.Infrastructure.Sql;

namespace DisputePortal.Api.Repository;
// Shared persistence authorization and locking primitives; no feature orchestration.
public abstract class RepositoryBase(IDatabaseConnectionFactory factory, IDatabaseDialect databaseDialect)
{
    protected readonly IDatabaseConnectionFactory connections = factory;
    protected readonly IDatabaseDialect dialect = databaseDialect;

    protected static long PageOffset(int page, int pageSize) => ((long)page - 1) * pageSize;

    protected static string? CleanSearch(string? search) => string.IsNullOrWhiteSpace(search) ? null : search.Trim()[..Math.Min(
        search.Trim().Length,
        100)];

    protected static string[] SplitAggregate(string values) => string.IsNullOrWhiteSpace(values) ? Array.Empty<string>() : values.Split(
        '|',
        StringSplitOptions.RemoveEmptyEntries);

    protected static readonly string StaffScopeSql = WorkflowAccess.CaseReadScope();
    protected static readonly string CaseSelect = SqlScripts.ReadSqlScript("Shared/Queries/Api/RepositoryBase/SelectCaseRoutingAssignmentAndDeadlines");
    protected const string AssignableRolesSql = "'dispute_agent','fraud_agent','card_specialist','debit_order_specialist','payments_specialist','operations_manager','super_admin'";
    protected static readonly string StaffSelect = SqlScripts.ReadSqlScript("Shared/Queries/Api/RepositoryBase/SelectStaffDirectoryWithCapacityAndWorkload");

    protected Task LockOperations(DbConnection c, DbTransaction tx, CancellationToken ct) =>
        DisputePortal.Platform.Infrastructure.RelationalDialect.LockOperations(c, tx, dialect.OperationsLockSql, ct);

    protected async Task<string[]> Roles(DbConnection c, DbTransaction? tx, Guid actor, CancellationToken ct) =>
        (await c.QueryAsync<string>(new CommandDefinition(
        SqlScripts.ReadSqlScript("Shared/Queries/Api/RepositoryBase/GetActiveUserRoles"),
        new { Id = actor },
        tx,
        cancellationToken: ct))).ToArray();

    protected async Task RequireAssistanceStaff(DbConnection c, DbTransaction? tx, Guid actor, CancellationToken ct)
    {
        if (!(await Roles(c, tx, actor, ct)).Any(role => CaseServicePolicy.IsSupervisor(role) || role == "service_clerk"))
        {
            throw new PortalForbiddenException("Customer assistance is restricted to authorised service staff.");
        }
    }

    protected async Task RequireSupervisor(DbConnection c, DbTransaction? tx, Guid actor, CancellationToken ct, bool superOnly = false)
    {
        var roles = await Roles(c, tx, actor, ct);
        if (!roles.Any(r => superOnly ? r == "super_admin" : CaseServicePolicy.IsSupervisor(r)))
        {
            throw new PortalForbiddenException();
        }
    }

    protected async Task EnsureCanWork(DbConnection c, DbTransaction tx, Guid actor, Guid id, CancellationToken ct)
    {
        var row = await c.QuerySingleOrDefaultAsync<CaseOperationsDto>(new CommandDefinition(
            SqlScripts.RenderSqlScript("Shared/Queries/Api/RepositoryBase/GetCaseWithinActorReadScope", ("StaffScopeSql", StaffScopeSql)),
            new { Id = id, ActorId = actor },
            tx,
            cancellationToken: ct));
        if (row is null)
        {
            throw new PortalNotFoundException();
        }

        var roles = await Roles(c, tx, actor, ct);
        if (!roles.Any(CaseServicePolicy.IsSupervisor)
            && !(row.AssignedTo == actor
            && roles.Any(r => CaseServicePolicy.CanWork(r, row.QueueType))))
        {
            throw new PortalForbiddenException("Only the assigned specialist or an operations supervisor can update this case.");
        }
    }

    protected async Task AuditOperation(
        DbConnection c,
        DbTransaction tx,
        Guid? actor,
        string action,
        Guid id,
        object details,
        CancellationToken ct,
        string outcome = "Success") => await c.ExecuteAsync(new CommandDefinition(
        SqlScripts.RenderSqlScript(
        "Shared/Queries/Api/RepositoryBase/InsertAuditEvent",
        ("AuditMetadataValueExpression", dialect.AuditMetadataValueExpression("@Metadata"))),
        new
        {
            Id = Guid.NewGuid(),
            Actor = actor,
            Action = action,
            EntityId = id,
            Outcome = outcome,
            OccurredAt = DateTimeOffset.UtcNow,
            Correlation = System.Diagnostics.Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString(),
            Metadata = JsonSerializer.Serialize(details)
        },
        tx,
        cancellationToken: ct));
}
