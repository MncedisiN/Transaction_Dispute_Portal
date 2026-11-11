using Dapper;
using System.Data.Common;
using Microsoft.Data.SqlClient;

namespace DisputePortal.Platform.Infrastructure;
// Only shared delivery infrastructure has provider-specific SQL. Each business service owns one repository/database.
public abstract class RelationalDialect
{
    public abstract DbConnection CreateDatabaseConnection(string connectionString);

    public abstract string BaseSchema { get; }
    public abstract string ClaimOutbox { get; }
    public abstract string PublishedOutbox { get; }
    public abstract string RetryOutbox { get; }
    public abstract string InsertOutbox { get; }
    public abstract string FindProcessed { get; }
    public abstract string AcquireMigrationLock { get; }
    public abstract string ReleaseMigrationLock { get; }
    public abstract string OperationsLockSql { get; }
    public abstract string EnableFinancialProjectionSql { get; }
    public abstract string DisableFinancialProjectionSql { get; }

    public static async Task LockOperations(DbConnection connection, DbTransaction transaction, string nativeSql, CancellationToken ct)
    {
        try
        {
            await connection.ExecuteAsync(new CommandDefinition(nativeSql, transaction: transaction, cancellationToken: ct));
        }
        catch (SqlException error) when (ct.IsCancellationRequested && error.Number == 0)
        {
            // SqlClient can surface an interrupted sp_getapplock as error 0
            // instead of TaskCanceledException. Retain the provider cause.
            throw new OperationCanceledException("The database operation was cancelled.", error, ct);
        }
    }

    public static RelationalDialect GetDatabaseDialect(string provider) => provider switch
    {
        "SqlServer" => new SqlServerDialect(),
        "PostgreSql" => new PostgreSqlDialect(),
        _ => throw new InvalidOperationException("Unknown database provider.")
    };
}
