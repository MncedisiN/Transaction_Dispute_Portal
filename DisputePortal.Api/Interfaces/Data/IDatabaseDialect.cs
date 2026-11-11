using System.Data.Common;
using DisputePortal.Api.Repository.Data;

namespace DisputePortal.Api.Interfaces.Data;

public interface IDatabaseDialect
{
    DatabaseProvider Provider { get; }

    string MigrationDirectoryName { get; }

    string SeedFileName { get; }

    string CreateMigrationsTableSql { get; }

    string AcquireMigrationLockSql { get; }

    string ReleaseMigrationLockSql { get; }

    string OperationsLockSql { get; }

    string TrueLiteral { get; }

    string FalseLiteral { get; }

    string ExistsAsBoolean(string selectSql);

    string EligibleTransactionExpression { get; }

    string AuditMetadataSelectExpression(string column);

    string AuditMetadataValueExpression(string parameter);

    string PaginationClause { get; }

    string LikeExpression(string column, string parameter);

    string DateTimeProjection(string column);

    string AddDaysToDateExpression(string column, string daysParameter);

    string InListExpression(string column, string parameter);

    bool IsUniqueConstraintViolation(DbException exception);
}
