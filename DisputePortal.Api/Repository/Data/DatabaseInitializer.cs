using System.Security.Cryptography;
using System.Text;
using Dapper;
using DisputePortal.Api.Configuration;
using DisputePortal.Api.Interfaces.Data;
using DisputePortal.Platform.Infrastructure.Sql;

namespace DisputePortal.Api.Repository.Data;

public class DatabaseInitializer(IDatabaseConnectionFactory connections, IDatabaseDialect dialect, ApiRuntimeOptions runtime)
{
    private string ScriptPath => Path.Combine(AppContext.BaseDirectory, "Database", dialect.Provider.ToString(), "Initialization", "CreateApiDatabase.sql");

    private async Task<(string Sql, string Checksum)> ReadInitializationScript(CancellationToken ct)
    {
        var sql = await File.ReadAllTextAsync(ScriptPath, ct);
        var normalized = sql.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        return (sql, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))));
    }

    public async Task ValidateSchema(CancellationToken ct)
    {
        var script = await ReadInitializationScript(ct);
        await using var connection = await connections.OpenConnection(ct);
        var checksum = await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            "SELECT checksum FROM database_installation WHERE component = @Component",
            new { Component = "Api" }, commandTimeout: runtime.MigrationCommandTimeoutSeconds, cancellationToken: ct));
        if (checksum != script.Checksum)
        {
            throw new InvalidOperationException("The database does not match this installation. Use its matching application release.");
        }
    }

    public async Task InitializeDatabase(CancellationToken ct)
    {
        var script = await ReadInitializationScript(ct);
        await using var connection = await connections.OpenConnection(ct);
        await connection.ExecuteAsync(new CommandDefinition(dialect.AcquireMigrationLockSql,
            commandTimeout: runtime.MigrationCommandTimeoutSeconds, cancellationToken: ct));
        try
        {
            await using var transaction = await connection.BeginTransactionAsync(ct);
            var metadata = dialect.Provider == DatabaseProvider.PostgreSql
                ? "CREATE TABLE IF NOT EXISTS database_installation (component varchar(40) PRIMARY KEY, checksum varchar(64) NOT NULL, installed_at timestamptz NOT NULL DEFAULT now())"
                : "IF OBJECT_ID('dbo.database_installation') IS NULL CREATE TABLE dbo.database_installation (component varchar(40) PRIMARY KEY, checksum varchar(64) NOT NULL, installed_at datetimeoffset NOT NULL DEFAULT SYSDATETIMEOFFSET())";
            await connection.ExecuteAsync(new CommandDefinition(metadata, transaction: transaction,
                commandTimeout: runtime.MigrationCommandTimeoutSeconds, cancellationToken: ct));
            var checksum = await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
                "SELECT checksum FROM database_installation WHERE component = @Component",
                new { Component = "Api" }, transaction, commandTimeout: runtime.MigrationCommandTimeoutSeconds, cancellationToken: ct));
            if (checksum is not null && checksum != script.Checksum)
            {
                throw new InvalidOperationException("The database already belongs to a different installation schema.");
            }
            if (checksum is null)
            {
                var batches = dialect.Provider == DatabaseProvider.SqlServer
                    ? System.Text.RegularExpressions.Regex.Split(script.Sql, @"(?im)^\s*GO\s*$")
                    : [script.Sql];
                foreach (var batch in batches.Where(value => !string.IsNullOrWhiteSpace(value)))
                {
                    await connection.ExecuteAsync(new CommandDefinition(batch, transaction: transaction,
                        commandTimeout: runtime.MigrationCommandTimeoutSeconds, cancellationToken: ct));
                }
                await connection.ExecuteAsync(new CommandDefinition(
                    "INSERT INTO database_installation (component, checksum) VALUES (@Component, @Checksum)",
                    new { Component = "Api", script.Checksum }, transaction,
                    commandTimeout: runtime.MigrationCommandTimeoutSeconds, cancellationToken: ct));
            }
            await transaction.CommitAsync(ct);
        }
        finally
        {
            await connection.ExecuteAsync(new CommandDefinition(dialect.ReleaseMigrationLockSql,
                commandTimeout: runtime.MigrationCommandTimeoutSeconds, cancellationToken: CancellationToken.None));
        }
    }
}
