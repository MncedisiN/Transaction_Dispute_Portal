using System.Data;
using System.Text.Json;
using Dapper;
using DisputePortal.Api.Caching;
using DisputePortal.Api.Models.Domain;
using DisputePortal.Api.Models.Exceptions;
using DisputePortal.Api.Models.Requests;
using DisputePortal.Api.Models.Responses;
using DisputePortal.Api.Repository.Data;
using DisputePortal.Platform.Contracts;
using DisputePortal.Platform.Infrastructure;
using DisputePortal.Api.Interfaces.Data;
using DisputePortal.Platform.Infrastructure.Sql;

namespace DisputePortal.Api.Repository;

public class CaseStatusRepository(IDatabaseConnectionFactory factory, IDatabaseDialect databaseDialect, PortalReferenceCache? cache = null) : RepositoryBase(
    factory,
    databaseDialect)
{
    private static readonly string RevisionSql = """
    SELECT
        SUM(revision)
    FROM disputes."caseStatuses"
    """;

    public async Task<CaseStatusCatalogue> GetWorkflowStatuses(CancellationToken ct)
    {
        if (cache is null)
        {
            return await LoadWorkflowStatusesFromDatabase(ct);
        }

        long revision;
        string authority;
        await using (var c = await connections.OpenConnection(ct))
        {
            revision = await c.QuerySingleAsync<long>(new CommandDefinition(RevisionSql, cancellationToken: ct));
            authority = $"{dialect.Provider}:{c.DataSource}:{c.Database}";
        }
         
        return await cache.GetReferenceData(
            "case-statuses",
            authority,
            revision.ToString(System.Globalization.CultureInfo.InvariantCulture),
            LoadWorkflowStatusesFromDatabase,
            ValidCatalogue,
            ct,
            value => value.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static bool ValidCatalogue(CaseStatusCatalogue value)
    {
        var expected = Enum.GetValues<DisputeStatus>();
        if (value.Revision <= 0
            || value.Items is null
            || value.Items.Length != expected.Length
            || value.Items.Select(x => x?.Code).Distinct(StringComparer.Ordinal).Count() != expected.Length)
        {
            return false;
        }

        foreach (var row in value.Items)
        {
            if (row is null
                || !Enum.TryParse<DisputeStatus>(row.Code, out var status)
                || !Enum.IsDefined(status)
                || status.ToString() != row.Code
                || row.DisplayName is not { Length: > 0 and <= 120 }
                || row.CustomerLabel is not { Length: > 0 and <= 120 }
                || row.NextStep is not { Length: > 0 and <= 500 }
                || row.Tone is not { Length: > 0 and <= 24 }
                || row.OwnerLabel is not { Length: <= 120 }
                || row.Stage is < 0 or > 2
                || row.SortOrder is < 0 or > 1000
                || row.Revision <= 0
                || row.Steps is not { Length: <= 16 }
                || row.Steps.Any(step => step is not { Length: > 0 and <= 120 })
                || row.Closed == DisputeWorkflow.IsActive(status)
                || row.WorkflowTargets is null)
            {
                return false;
            }

            var targets = expected.Where(target => DisputeWorkflow.CanTransition(status, target)).Select(target => target.ToString());
            if (!targets.SequenceEqual(row.WorkflowTargets))
            {
                return false;
            }
        }

        return value.Items.Sum(row => (decimal)row.Revision) == value.Revision;
    }

    private async Task<CaseStatusCatalogue> LoadWorkflowStatusesFromDatabase(CancellationToken ct)
    {
        await using var c = await connections.OpenConnection(ct);
        await using var tx = await c.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var revision = await c.QuerySingleAsync<long>(new CommandDefinition(RevisionSql, transaction: tx, cancellationToken: ct));
        var steps = dialect.Provider == DatabaseProvider.PostgreSql ? "steps::text" : "steps";
        var rows = (await c.QueryAsync<CaseStatusDefinition>(new CommandDefinition(
            SqlScripts.RenderSqlScript("Shared/Queries/Api/CaseStatusRepository/GetCaseStatuses", ("Steps", steps)),
            transaction: tx,
            cancellationToken: ct))).ToArray();
        foreach (var row in rows)
        {
            if (!Enum.TryParse<DisputeStatus>(row.Code, out var status) || !Enum.IsDefined(status) || status.ToString() != row.Code)
            {
                throw new InvalidOperationException("An unsupported workflow code requires deployment review.");
            }

            row.Steps = JsonSerializer.Deserialize<string[]>(row.StepsJson) ?? [];
            row.Closed = !DisputeWorkflow.IsActive(status);
            row.WorkflowTargets = Enum.GetValues<DisputeStatus>().Where(target => DisputeWorkflow.CanTransition(status, target)).Select(target => target.ToString()).ToArray();
        }

        await tx.CommitAsync(ct);
        return new()
        {
            Revision = revision,
            Items = rows
        };
    }

    public async Task UpdateWorkflowStatus(Guid actor, string code, UpdateCaseStatusRequest request, CancellationToken ct)
    {
        if (!Enum.TryParse<DisputeStatus>(code, out var status) || !Enum.IsDefined(status) || status.ToString() != code)
        {
            throw new PortalNotFoundException();
        }

        if (string.IsNullOrWhiteSpace(request.DisplayName)
            || request.DisplayName.Trim().Length > 120
            || string.IsNullOrWhiteSpace(request.CustomerLabel)
            || request.CustomerLabel.Trim().Length > 120
            || string.IsNullOrWhiteSpace(request.NextStep)
            || request.NextStep.Trim().Length > 500
            || string.IsNullOrWhiteSpace(request.Reason)
            || request.Reason.Trim().Length is < 10 or > 500
            || request.SortOrder is < 0 or > 1000)
        {
            throw new PortalValidationException("Use valid workflow descriptions and a change reason of 10–500 characters.");
        }

        await using var c = await connections.OpenConnection(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await LockOperations(c, tx, ct);
        await RequireSupervisor(c, tx, actor, ct, true);
        var previous = await c.QuerySingleAsync<long>(new CommandDefinition(RevisionSql, transaction: tx, cancellationToken: ct));
        if (previous != request.ExpectedRevision)
        {
            throw new PortalConflictException("Workflow descriptions changed. Your input is retained; refresh the catalogue and review it again.");
        }

        var affected = await c.ExecuteAsync(new CommandDefinition(
            """
            UPDATE disputes."caseStatuses"
            SET
                "displayName" = @DisplayName,
                "customerLabel" = @CustomerLabel,
                "sortOrder" = @SortOrder,
                "nextStep" = @NextStep
            WHERE code = @Code
            """,
            new
            {
                Code = code,
                DisplayName = request.DisplayName.Trim(),
                CustomerLabel = request.CustomerLabel.Trim(),
                request.SortOrder,
                NextStep = request.NextStep.Trim()
            },
            tx,
            cancellationToken: ct));
        // SQL Server includes the revision trigger update in the affected count.
        // The immutable primary key still limits the target update to one code.
        if (affected < 1)
        {
            throw new PortalNotFoundException();
        }

        var current = await c.QuerySingleAsync<long>(new CommandDefinition(RevisionSql, transaction: tx, cancellationToken: ct));
        await AuditOperation(
            c,
            tx,
            actor,
            "CaseStatusDescriptionsChanged",
            Guid.Empty,
            new
            {
                Code = code,
                PreviousRevision = previous,
                Revision = current,
                request.Reason
            },
            ct);
        await ServiceDatabase.SaveDomainEventToOutbox(
            c,
            tx,
            DomainEvent.CreateDomainEvent(
                "identity",
                EventTypes.CaseStatusesChanged,
                new { PreviousRevision = previous, Revision = current }),
            ct);
        await tx.CommitAsync(ct); 
    }
}
