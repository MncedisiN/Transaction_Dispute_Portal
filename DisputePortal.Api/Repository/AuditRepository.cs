using Dapper;
using DisputePortal.Api.Models.Responses;
using DisputePortal.Api.Interfaces.Data;
using DisputePortal.Api.Interfaces.Repositories;
using DisputePortal.Platform.Infrastructure.Sql;

namespace DisputePortal.Api.Repository; 
public class AuditRepository(IDatabaseConnectionFactory factory, IDatabaseDialect databaseDialect) : RepositoryBase(
    factory,
    databaseDialect), IAuditRepository
{
    public async Task<PageResult<AuditRecordDto>> GetAuditLogs(int page, int pageSize, string? action, CancellationToken ct)
    {
        var union = SqlScripts.RenderSqlScript(
            "Shared/Queries/Api/AuditRepository/GetAuditEvents",
            ("AuditMetadataSelectExpression", dialect.AuditMetadataSelectExpression("audit_event.\"safeMetadata\"")));
        var where = SqlScripts.RenderSqlScript("Shared/Queries/Api/AuditRepository/JoinAuditDetails", ("Union", union));
        var select = SqlScripts.ReadSqlScript("Shared/Queries/Api/AuditRepository/SelectAuditColumns");
        await using var c = await connections.OpenConnection(ct);
        List<AuditRow> rows;
        long total;
        using (var result = await c.QueryMultipleAsync(new CommandDefinition(
            SqlScripts.RenderSqlScript(
            "Shared/Queries/Api/AuditRepository/GetAuditPage",
            ("SelectQuery", select),
            ("FilterClause", where),
            ("PaginationClause", dialect.PaginationClause),
            ("RelatedFilterClause", where)),
            new
            {
                Action = string.IsNullOrWhiteSpace(action) ? null : action.Trim(),
                Offset = PageOffset(page, pageSize),
                PageSize = pageSize
            },
            cancellationToken: ct)))
        {
            rows = (await result.ReadAsync<AuditRow>()).AsList();
            total = await result.ReadSingleAsync<long>();
        }

        var identities = new Dictionary<(string Source, Guid Id), ActionActorDto>();
        foreach (var group in rows.GroupBy(row => row.Source))
            foreach (var pair in await ActorIdentityReader.GetRecordedActorIdentities(
            c,
            null,
            dialect,
            group.Key,
            group.Select(row => row.Id),
            true,
            ct))
                identities[(group.Key, pair.Key)] = pair.Value;
        var items = rows.Select(row =>
        {
            identities.TryGetValue((row.Source, row.Id), out var identity);
            return new AuditRecordDto(
                row.Id,
                identity?.Label ?? "Actor not recorded",
                row.Action,
                row.EntityType,
                row.EntityId,
                row.OccurredAt,
                row.CorrelationId,
                row.SafeMetadata,
                row.Outcome,
                identity,
                row.Source);
        }).ToArray();
        return new(items, page, pageSize, total);
    }

    private class AuditRow
    {
        public Guid Id { get; set; }
        public string Source { get; set; } = "";
        public string Action { get; set; } = "";
        public string EntityType { get; set; } = "";
        public Guid? EntityId { get; set; }
        public DateTimeOffset OccurredAt { get; set; }
        public string CorrelationId { get; set; } = "";
        public string SafeMetadata { get; set; } = "{}";
        public string Outcome { get; set; } = "";
    }
}
