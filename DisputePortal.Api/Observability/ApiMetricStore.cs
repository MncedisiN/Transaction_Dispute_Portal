using Dapper;
using DisputePortal.Api.Repository.Data;
using DisputePortal.Api.Models.Domain;
using DisputePortal.Api.Interfaces.Observability;
using DisputePortal.Api.Interfaces.Data;
using DisputePortal.Platform.Infrastructure.Sql;

namespace DisputePortal.Api.Observability;

public class ApiMetricStore(IDatabaseConnectionFactory connections, IDatabaseDialect dialect, TelemetryOptions options) : IApiMetricStore
{
    public async Task Persist(MetricBatch batch, CancellationToken ct)
    {
        if (batch.Summaries.Count == 0)
        {
            return;
        }

        await using var c = await connections.OpenConnection(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        var lockSql = dialect.Provider == DatabaseProvider.SqlServer ? SqlScripts.ReadSqlScript("SqlServer/Queries/Api/ApiMetricStore/AcquireOperationLock") : """
        SELECT
            pg_advisory_xact_lock(hashtextextended('DisputePortal.ApiMetricSummaries', 0))
        """;
        CommandDefinition Command(string sql, object? args = null) => new(
            sql,
            args,
            tx,
            commandTimeout: options.StorageTimeoutSeconds,
            cancellationToken: ct);
        await c.ExecuteAsync(Command(lockSql));
        if (await c.ExecuteScalarAsync<int>(Command(
            """
            SELECT
                COUNT(*)
            FROM monitoring."metricBatches"
            WHERE "publicId" = @Id
            """,
            new { batch.Id })) != 0)
        {
            await tx.CommitAsync(ct);
            return;
        }

        foreach (var row in batch.Summaries)
        {
            var args = new
            {
                row.Key.BucketStart,
                HttpMethod = row.Key.Method,
                row.Key.Route,
                row.Key.StatusCode,
                row.RequestCount,
                row.FailureCount,
                row.ClientErrorCount,
                row.TotalDurationMs,
                row.MinimumDurationMs,
                row.MaximumDurationMs,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            var changed = await c.ExecuteAsync(Command(
                SqlScripts.ReadSqlScript("Shared/Queries/Api/ApiMetricStore/UpdateApiMetricSummaries"),
                args));
            if (changed == 0)
            {
                await c.ExecuteAsync(Command(SqlScripts.ReadSqlScript("Shared/Queries/Api/ApiMetricStore/InsertApiMetricSummary"), args));
            }
        }

        await c.ExecuteAsync(Command(
            """
            INSERT INTO monitoring."metricBatches"(
                "publicId",
                "createdAt"
            )
            VALUES (
                @Id,
                @Now
            )
            """,
            new { batch.Id, Now = DateTimeOffset.UtcNow }));
        await tx.CommitAsync(ct);
    }

    public async Task<MonitoringOverview> Overview(DateTimeOffset from, DateTimeOffset to, int limit, CancellationToken ct)
    {
        await using var c = await connections.OpenConnection(ct);
        var columns = SqlScripts.ReadSqlScript("Shared/Queries/Api/ApiMetricStore/SelectMetricSummaryAggregateColumns");
        var sql = SqlScripts.RenderSqlScript(
            dialect.Provider == DatabaseProvider.SqlServer ? "SqlServer/Queries/Api/ApiMetricStore/GetApproximateMonitoringOverview" : "Shared/Queries/Api/ApiMetricStore/CalculateApiMetricSummaries",
            ("SelectedColumns", columns),
            ("RelatedSelectedColumns", columns));
        using var results = await c.QueryMultipleAsync(new CommandDefinition(
            sql,
            new
            {
                From = from,
                To = to,
                Limit = Math.Clamp(limit, 1, 500)
            },
            commandTimeout: options.StorageTimeoutSeconds,
            cancellationToken: ct));
        var totals = await results.ReadSingleAsync<Totals>();
        var endpoints = (await results.ReadAsync<StoredMetric>()).AsList();
        return new(
            from,
            to,
            totals.RequestCount,
            totals.FailureCount,
            totals.ClientErrorCount,
            totals.RequestCount == 0 ? 0 : totals.TotalDurationMs / totals.RequestCount,
            totals.LastPersistedAt,
            endpoints);
    }

    public async Task Prune(DateTimeOffset before, CancellationToken ct)
    {
        await using var c = await connections.OpenConnection(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await c.ExecuteAsync(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/ApiMetricStore/DeleteApiMetricSummaries"),
            new { Before = before, BatchBefore = before.AddDays(-1) },
            transaction: tx,
            commandTimeout: options.StorageTimeoutSeconds,
            cancellationToken: ct));
        await tx.CommitAsync(ct);
    }

    private class Totals : StoredMetric
    {
        public DateTimeOffset? LastPersistedAt { get; set; }
    }
}
