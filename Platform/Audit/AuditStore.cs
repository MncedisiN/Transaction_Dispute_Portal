using System.Text.Json;
using Dapper;
using DisputePortal.Platform.Contracts;
using DisputePortal.Platform.Infrastructure;
using DisputePortal.Platform.Audit.Interfaces;
using DisputePortal.Platform.Infrastructure.Sql;

namespace DisputePortal.Platform.Audit;

public class AuditStore(ServiceDatabase db) : IAuditStore
{
    public static readonly string Schema = SqlScripts.ReadSqlScript("PostgreSql/Schema/Audit/AuditStore/CreateAuditSchema");

    public Task Append(DomainEvent e, CancellationToken ct) => db.ProcessEventWithInboxDeduplication(
        e,
        async (c, tx) =>
    {
        await c.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO audit."auditEvents"(
                    "publicId",
                    "correlationPublicId",
                    "eventType",
                    "sourceService",
                    "eventBody"
                )
                VALUES (
                    @EventId,
                    @CorrelationId,
                    @EventType,
                    @SourceService,
                    CAST(@Body AS jsonb)
                )
                """,
                new
                {
                    e.EventId,
                    e.CorrelationId,
                    e.EventType,
                    e.SourceService,
                    Body = JsonSerializer.Serialize(e, DomainEvent.Json)
                },
                tx,
                cancellationToken: ct));
    },
        ct);

    public async Task<object> GetAuditEntries(Guid? correlation, CancellationToken ct)
    {
        await using var c = await db.OpenServiceDatabaseConnection(ct);
        return (await c.QueryAsync<string>(new CommandDefinition(
            """
            SELECT
                "eventBody"::text
            FROM audit."auditEvents"
            WHERE (@Id IS NULL
                OR "correlationPublicId" = @Id)
            ORDER BY "receivedAt" DESC, "publicId" DESC
            LIMIT 500
            """,
            new
            {
                Id = correlation
            },
            cancellationToken: ct))).Select(s => JsonSerializer.Deserialize<DomainEvent>(s, DomainEvent.Json)).ToArray();
    }
}
