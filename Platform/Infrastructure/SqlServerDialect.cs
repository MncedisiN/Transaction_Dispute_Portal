using System.Data.Common;
using Microsoft.Data.SqlClient;
using DisputePortal.Platform.Infrastructure.Sql;

namespace DisputePortal.Platform.Infrastructure;

public class SqlServerDialect : RelationalDialect
{
   
    public override string OperationsLockSql => SqlScripts.ReadSqlScript("SqlServer/Queries/Infrastructure/SqlServerDialect/AcquireOperationLock");
    public override string EnableFinancialProjectionSql => SqlScripts.ReadSqlScript("SqlServer/Queries/Infrastructure/SqlServerDialect/EnableFinancialProjectionWrites");
    public override string DisableFinancialProjectionSql => SqlScripts.ReadSqlScript("SqlServer/Queries/Infrastructure/SqlServerDialect/DisableFinancialProjectionWrites");

    public override DbConnection CreateDatabaseConnection(string cs) => new SqlConnection(cs);

    public override string AcquireMigrationLock => SqlScripts.ReadSqlScript("SqlServer/Queries/Infrastructure/SqlServerDialect/AcquireMigrationLock");
    public override string ReleaseMigrationLock => SqlScripts.ReadSqlScript("SqlServer/Queries/Infrastructure/SqlServerDialect/ReleaseMigrationLock");
    public override string FindProcessed => SqlScripts.ReadSqlScript("SqlServer/Queries/Infrastructure/SqlServerDialect/GetServiceInbox");
    public override string InsertOutbox => """
    INSERT INTO service_outbox (
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
        @Body,
        @Correlation,
        SYSUTCDATETIME()
    )
    """;
    public override string ClaimOutbox => SqlScripts.ReadSqlScript("SqlServer/Queries/Infrastructure/SqlServerDialect/ClaimNextOutboxEvent");
    public override string PublishedOutbox => """
    UPDATE service_outbox
    SET
        state = 'Published',
        published_at = SYSUTCDATETIME(),
        lease_id = NULL,
        lease_until = NULL,
        last_error = NULL
    WHERE event_id = @Id
    AND lease_id = @Lease
    """;
    public override string RetryOutbox => SqlScripts.ReadSqlScript("SqlServer/Queries/Infrastructure/SqlServerDialect/ScheduleOutboxEventRetry");
    public override string BaseSchema => SqlScripts.ReadSqlScript("SqlServer/Schema/Infrastructure/SqlServerDialect/CreateServiceInfrastructureSchema");
}
