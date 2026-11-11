using System.Data.Common;
using Microsoft.Data.SqlClient;
using Npgsql;
using DisputePortal.Api.Interfaces.Data;
using DisputePortal.Platform.Infrastructure.Sql;

namespace DisputePortal.Api.Repository.Data;

public class DatabaseDialect : IDatabaseDialect
{
    private const long PostgreSqlLockKey = 835129174;

    private DatabaseDialect(DatabaseProvider provider)
    {
        Provider = provider;
        OperationsLockSql = DisputePortal.Platform.Infrastructure.RelationalDialect.GetDatabaseDialect(provider.ToString()).OperationsLockSql;
    }

    public string OperationsLockSql { get; }
    public DatabaseProvider Provider { get; }
    public string MigrationDirectoryName => Provider == DatabaseProvider.PostgreSql ? "PostgreSql" : "SqlServer";
    public string SeedFileName => Provider == DatabaseProvider.PostgreSql ? "seed.postgresql.sql" : "seed.sqlserver.sql";
    public string CreateMigrationsTableSql => Provider switch
    {
        DatabaseProvider.PostgreSql => SqlScripts.ReadSqlScript("PostgreSql/Schema/Api/DatabaseDialect/CreateMigrationHistoryTable"),
        DatabaseProvider.SqlServer => SqlScripts.ReadSqlScript("SqlServer/Schema/Api/DatabaseDialect/CreateMigrationHistoryTable"),
        _ => throw new ArgumentOutOfRangeException()
    };
    public string AcquireMigrationLockSql => Provider switch
    {
        DatabaseProvider.PostgreSql => SqlScripts.RenderSqlScript(
        "PostgreSql/Queries/Api/DatabaseDialect/AcquireMigrationLock",
        ("PostgreSqlLockKey", PostgreSqlLockKey.ToString(System.Globalization.CultureInfo.InvariantCulture))),
        DatabaseProvider.SqlServer => SqlScripts.ReadSqlScript("SqlServer/Queries/Api/DatabaseDialect/AcquireMigrationLock"),
        _ => throw new ArgumentOutOfRangeException()
    };
    public string ReleaseMigrationLockSql => Provider switch
    {
        DatabaseProvider.PostgreSql => SqlScripts.RenderSqlScript(
        "PostgreSql/Queries/Api/DatabaseDialect/ReleaseMigrationLock",
        ("PostgreSqlLockKey", PostgreSqlLockKey.ToString(System.Globalization.CultureInfo.InvariantCulture))),
        DatabaseProvider.SqlServer => SqlScripts.ReadSqlScript("Shared/Queries/Api/DatabaseDialect/ReleaseMigrationLock"),
        _ => throw new ArgumentOutOfRangeException()
    };
    public string FalseLiteral => Provider == DatabaseProvider.PostgreSql ? "FALSE" : SqlScripts.ReadSqlScript("SqlServer/Queries/Api/DatabaseDialect/EvaluateFalseBooleanLiteral");
    public string TrueLiteral => Provider == DatabaseProvider.PostgreSql ? "TRUE" : SqlScripts.ReadSqlScript("SqlServer/Queries/Api/DatabaseDialect/EvaluateTrueBooleanLiteral");
    public string PaginationClause => SqlScripts.ReadSqlScript("Shared/Queries/Api/DatabaseDialect/ApplyOffsetPagination");

    public string LikeExpression(string column, string parameter) => Provider == DatabaseProvider.PostgreSql ? SqlScripts.RenderSqlScript(
        "PostgreSql/Queries/Api/DatabaseDialect/BuildCaseInsensitiveContainsPredicate",
        ("Column", column),
        ("Parameter", parameter)) : $"LOWER({column}) LIKE '%' + {parameter} + '%'";

    public string AddDaysToDateExpression(string column, string daysParameter) =>
        Provider == DatabaseProvider.PostgreSql
            ? $"({column} + {daysParameter} * INTERVAL '1 day')"
            : $"DATEADD(day, {daysParameter}, {column})";

    public string InListExpression(string column, string parameter) =>
        Provider == DatabaseProvider.PostgreSql ? $"{column} = ANY({parameter})" : SqlScripts.RenderSqlScript(
        "SqlServer/Queries/Api/DatabaseDialect/BuildInListPredicate",
        ("Column", column),
        ("Parameter", parameter));

    public string ExistsAsBoolean(string selectSql) => Provider == DatabaseProvider.PostgreSql ? SqlScripts.RenderSqlScript(
        "PostgreSql/Queries/Api/DatabaseDialect/ConvertExistenceToBoolean",
        ("SelectSql", selectSql)) : SqlScripts.RenderSqlScript(
        "SqlServer/Queries/Api/DatabaseDialect/ConvertExistenceToBoolean",
        ("SelectSql", selectSql));

    private string EligibleTransactionPredicate => SqlScripts.RenderSqlScript(
        "Shared/Queries/Api/DatabaseDialect/CheckTransactionDisputeEligibility",
        ("TrueValue", TrueLiteral));
    public string EligibleTransactionExpression => Provider == DatabaseProvider.PostgreSql ? SqlScripts.RenderSqlScript(
        "PostgreSql/Queries/Api/DatabaseDialect/EvaluateTransactionDisputeEligibility",
        ("EligibleTransactionPredicate", EligibleTransactionPredicate)) : SqlScripts.RenderSqlScript(
        "SqlServer/Queries/Api/DatabaseDialect/EvaluateTransactionDisputeEligibility",
        ("EligibleTransactionPredicate", EligibleTransactionPredicate));

    public string AuditMetadataSelectExpression(string column) =>
        Provider == DatabaseProvider.PostgreSql ? $"{column}::text" : SqlScripts.RenderSqlScript(
        "SqlServer/Queries/Api/DatabaseDialect/ConvertAuditMetadataToText",
        ("Column", column));

    public string AuditMetadataValueExpression(string parameter) =>
        Provider == DatabaseProvider.PostgreSql ? $"{parameter}::jsonb" : parameter;

    public bool IsUniqueConstraintViolation(DbException exception) => Provider switch
    {
        DatabaseProvider.PostgreSql => exception is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation },
        DatabaseProvider.SqlServer => exception is SqlException { Number: 2601 or 2627 },
        _ => false
    };

    public string DateTimeProjection(string column) => SqlScripts.RenderSqlScript(
        "Shared/Queries/Api/DatabaseDialect/ProjectUtcDateTime",
        ("Column", column),
        ("SqlExpression", Provider == DatabaseProvider.PostgreSql ? "timestamp" : "datetime2"));

    public static DatabaseProvider ParseProvider(string? name) => name?.Trim().ToLowerInvariant() switch
    {
        null or "" or "postgresql" or "postgres" or "pgsql" => DatabaseProvider.PostgreSql,
        "sqlserver" or "mssql" or "microsoft sql server" => DatabaseProvider.SqlServer,
        _ => throw new InvalidOperationException("Database:Provider must be PostgreSql or SqlServer.")
    };

    public static IDatabaseDialect GetDatabaseDialect(DatabaseProvider provider) => new DatabaseDialect(provider);
}
