using System.Data.Common;
using Npgsql;
using DisputePortal.Platform.Infrastructure.Sql;

namespace DisputePortal.Platform.Infrastructure;

public class PostgreSqlDialect : RelationalDialect
{
    public override string OperationsLockSql => """
    SELECT
        pg_advisory_xact_lock(hashtextextended('DisputePortalOperations', 0))
    """;
    public override string EnableFinancialProjectionSql => """
    SELECT
        set_config('dispute_portal.canonical_projection', 'on', TRUE)
    """;
    public override string DisableFinancialProjectionSql => """
    SELECT
        set_config('dispute_portal.canonical_projection', 'off', TRUE)
    """;

    public override DbConnection CreateDatabaseConnection(string cs) => new NpgsqlConnection(cs);

    public override string AcquireMigrationLock => """
    SELECT
        pg_advisory_lock(hashtext('platform-migrations'))
    """;
    public override string ReleaseMigrationLock => """
    SELECT
        pg_advisory_unlock(hashtext('platform-migrations'))
    """;
    public override string FindProcessed => SqlScripts.ReadSqlScript("PostgreSql/Queries/Infrastructure/PostgreSqlDialect/GetServiceInbox");
    public override string InsertOutbox => """
    INSERT INTO service_outbox(
        event_id,
        event_type,
        event_version,
        body,
        correlation_id,
        next_attempt_at
    )
    VALUES (
        @Id,
        @Type,
        1,
        CAST(@Body AS jsonb),
        @Correlation,
        now()
    )
    """;
    public override string ClaimOutbox => SqlScripts.ReadSqlScript("PostgreSql/Queries/Infrastructure/PostgreSqlDialect/ClaimNextOutboxEvent");
    public override string PublishedOutbox => """
    UPDATE service_outbox
    SET
        state = 'Published',
        published_at = now(),
        lease_id = NULL,
        lease_until = NULL,
        last_error = NULL
    WHERE event_id = @Id
    AND lease_id = @Lease
    """;
    public override string RetryOutbox => SqlScripts.ReadSqlScript("PostgreSql/Queries/Infrastructure/PostgreSqlDialect/ScheduleOutboxEventRetry");
    public override string BaseSchema => SqlScripts.ReadSqlScript("PostgreSql/Schema/Infrastructure/PostgreSqlDialect/CreateServiceInfrastructureSchema");
}
