using System.Data.Common;
using Microsoft.Data.SqlClient;
using Npgsql;
using DisputePortal.Api.Interfaces.Data;
using DisputePortal.Platform.Infrastructure.Sql;

namespace DisputePortal.Api.Repository.Data;

public class DatabaseConnectionFactory(DatabaseProvider provider, string connectionString) : IDatabaseConnectionFactory
{
    static DatabaseConnectionFactory()
    {
        Dapper.SqlMapper.AddTypeHandler(new UtcTimestampHandler());
    }

    public DatabaseProvider Provider { get; } = provider;

    public async Task<DbConnection> OpenConnection(CancellationToken cancellationToken)
    {
        DbConnection connection = Provider switch
        {
            DatabaseProvider.PostgreSql => new NpgsqlConnection(connectionString),
            DatabaseProvider.SqlServer => new SqlConnection(connectionString),
            _ => throw new ArgumentOutOfRangeException(nameof(Provider))
        };
        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private async Task EnsurePostgreSqlDatabase(CancellationToken ct)
    {
        var settings = new NpgsqlConnectionStringBuilder(connectionString);
        var databaseName = settings.Database;
        if (string.IsNullOrWhiteSpace(databaseName) || System.Text.Encoding.UTF8.GetByteCount(databaseName) > 63)
        {
            throw new InvalidOperationException("The PostgreSQL connection string must specify a database name of at most 63 bytes.");
        }

        settings.Database = "postgres";
        await using var connection = new NpgsqlConnection(settings.ConnectionString);
        await connection.OpenAsync(ct);
        // CREATE DATABASE must run outside a transaction. Serialize cooperating
        // development hosts using a session lock in the maintenance database.
        await using var acquire = new NpgsqlCommand(
            """
            SELECT
                pg_advisory_lock(hashtext(@Name))
            """,
            connection)
        {
            CommandTimeout = 60
        };
        acquire.Parameters.AddWithValue("Name", "DisputePortal.Database." + databaseName);
        await acquire.ExecuteNonQueryAsync(ct);
        try
        {
            await using var exists = new NpgsqlCommand(
                SqlScripts.ReadSqlScript("Shared/Queries/Api/DatabaseConnectionFactory/CheckPgDatabaseExist"),
                connection);
            exists.Parameters.AddWithValue("Name", databaseName);
            if (await exists.ExecuteScalarAsync(ct) is true)
            {
                return;
            }

            using var identifiers = new NpgsqlCommandBuilder();
            await using var create = new NpgsqlCommand(
                SqlScripts.RenderSqlScript(
                "Shared/Queries/Api/DatabaseConnectionFactory/CreateApplicationDatabase",
                ("QuoteIdentifier", identifiers.QuoteIdentifier(databaseName))),
                connection)
            {
                CommandTimeout = 120
            };
            try
            {
                await create.ExecuteNonQueryAsync(ct);
            }
            catch (PostgresException error) when (error.SqlState == PostgresErrorCodes.DuplicateDatabase)
            {
                // An external provisioning process may not use our advisory lock.
                // Only accept its result after confirming that the database exists.
                if (await exists.ExecuteScalarAsync(ct) is not true)
                {
                    throw;
                }
            }
        }
        finally
        {
            await using var release = new NpgsqlCommand(
                """
                SELECT
                    pg_advisory_unlock(hashtext(@Name))
                """,
                connection)
            {
                CommandTimeout = 15
            };
            release.Parameters.AddWithValue("Name", "DisputePortal.Database." + databaseName);
            await release.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    public async Task EnsureDatabaseExists(CancellationToken cancellationToken)
    {
        if (Provider == DatabaseProvider.PostgreSql)
        {
            await EnsurePostgreSqlDatabase(cancellationToken);
            return;
        }

        var builder = new SqlConnectionStringBuilder(connectionString);
        var databaseName = builder.InitialCatalog;
        if (string.IsNullOrWhiteSpace(databaseName) || databaseName.Length > 128)
        {
            throw new InvalidOperationException("The SQL Server connection string must specify a database name.");
        }

        builder.InitialCatalog = "master";
        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var acquire = connection.CreateCommand();
        acquire.CommandText = SqlScripts.ReadSqlScript("Shared/Queries/Api/DatabaseConnectionFactory/AcquireOperationLockForEnsureDatabaseExists");
        acquire.CommandTimeout = 65;
        acquire.Parameters.AddWithValue("@Resource", "DisputePortal.Database." + databaseName);
        await acquire.ExecuteNonQueryAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = SqlScripts.ReadSqlScript("SqlServer/Queries/Api/DatabaseConnectionFactory/CreateSqlServerDatabaseIfMissing");
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@DatabaseName";
            parameter.Value = databaseName;
            command.Parameters.Add(parameter);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            await using var release = connection.CreateCommand();
            release.CommandText = SqlScripts.ReadSqlScript("Shared/Queries/Api/DatabaseConnectionFactory/ReleaseMigrationLock");
            release.CommandTimeout = 15;
            release.Parameters.AddWithValue("@Resource", "DisputePortal.Database." + databaseName);
            await release.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    private class UtcTimestampHandler : Dapper.SqlMapper.TypeHandler<DateTimeOffset>
    {
        public override DateTimeOffset Parse(object value) => value switch
        {
            DateTimeOffset offset => offset,
            DateTime date => new DateTimeOffset(DateTime.SpecifyKind(date, DateTimeKind.Utc)),
            _ => throw new InvalidCastException("Unsupported database timestamp type.")
        };

        public override void SetValue(System.Data.IDbDataParameter parameter, DateTimeOffset value) =>
            parameter.Value = value.ToUniversalTime();
    }
}
