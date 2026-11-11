using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Dapper;
using DisputePortal.Api.Repository.Data;
using Microsoft.Data.SqlClient;
using Npgsql;
using Xunit;
using DisputePortal.Api.Interfaces.Data;

namespace DisputePortal.IntegrationTests;
/// <summary>
/// Optional native-only acceleration. The shared database contains migration
/// reference data only; every fact still owns a separate disposable database.
/// </summary>
public sealed class IntegrationDatabaseTemplate : IAsyncLifetime
{
    private const int CommandTimeoutSeconds = 180;
    private readonly string _referenceName = "DisputePortal_Template_" + Guid.NewGuid().ToString("N");
    private string _adminConnectionString = "";
    private string _referenceConnectionString = "";
    private string? _catalogueHash;
    private string? _backupPath;
    private string? _dataDirectory;
    private string? _logDirectory;
    private string? _owner;
    private bool _created;
    public bool Enabled { get; } = Environment.GetEnvironmentVariable("PORTAL_TEST_USE_TEMPLATE") == "1";
    public DatabaseProvider Provider { get; private set; }

    public async Task InitializeAsync()
    {
        if (!Enabled)
        {
            return;
        }

        var sql = Environment.GetEnvironmentVariable("PORTAL_TEST_SQLSERVER");
        var postgres = Environment.GetEnvironmentVariable("PORTAL_TEST_POSTGRESQL");
        if (!string.IsNullOrWhiteSpace(sql))
        {
            Provider = DatabaseProvider.SqlServer;
            _adminConnectionString = new SqlConnectionStringBuilder(sql)
            {
                InitialCatalog = "master"
            }.ConnectionString;
            _referenceConnectionString = new SqlConnectionStringBuilder(sql)
            {
                InitialCatalog = _referenceName,
                CommandTimeout = CommandTimeoutSeconds
            }.ConnectionString;
        }
        else if (!string.IsNullOrWhiteSpace(postgres))
        {
            Provider = DatabaseProvider.PostgreSql;
            _adminConnectionString = new NpgsqlConnectionStringBuilder(postgres)
            {
                Database = "postgres"
            }.ConnectionString;
            _referenceConnectionString = new NpgsqlConnectionStringBuilder(postgres)
            {
                Database = _referenceName
            }.ConnectionString;
        }
        else
        {
            throw new InvalidOperationException("Native template mode requires a configured native test administrator.");
        }

        _catalogueHash = CatalogueHash();
        try
        {
            await CreateReference(CancellationToken.None);
            var connections = new DatabaseConnectionFactory(Provider, _referenceConnectionString);
            var migrator = new DatabaseInitializer(
                connections,
                DatabaseDialect.GetDatabaseDialect(Provider),
                DisputePortal.TestFixtures.TestRuntimeConfiguration.Read());
            await migrator.InitializeDatabase(CancellationToken.None);
            await migrator.ValidateSchema(CancellationToken.None);
            await AssertEmptyBusinessData(connections, CancellationToken.None);
            EnsureCatalogueUnchanged();
            if (Provider == DatabaseProvider.SqlServer)
            {
                await PrepareSqlBackup(CancellationToken.None);
            }
            else
            {
                await SealPostgresReference(CancellationToken.None);
            }
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    public async Task<IDatabaseConnectionFactory> Clone(string databaseName, CancellationToken ct)
    {
        if (!Enabled || !_created || _catalogueHash is null)
        {
            throw new InvalidOperationException("The native test template is unavailable.");
        }

        ValidateName(databaseName, "DisputePortal_Test_");
        EnsureCatalogueUnchanged();
        IDatabaseConnectionFactory connections;
        if (Provider == DatabaseProvider.SqlServer)
        {
            await CloneSql(databaseName, ct);
            connections = new DatabaseConnectionFactory(
                Provider,
                new SqlConnectionStringBuilder(_adminConnectionString)
                {
                    InitialCatalog = databaseName,
                    CommandTimeout = CommandTimeoutSeconds
                }.ConnectionString);
        }
        else
        {
            await ClonePostgres(databaseName, ct);
            connections = new DatabaseConnectionFactory(
                Provider,
                new NpgsqlConnectionStringBuilder(_adminConnectionString) { Database = databaseName }.ConnectionString);
        }

        // Validate the actual clone, not merely the source template or a hash.
        await new DatabaseInitializer(
            connections,
            DatabaseDialect.GetDatabaseDialect(Provider),
            DisputePortal.TestFixtures.TestRuntimeConfiguration.Read()).ValidateSchema(ct);
        await AssertEmptyBusinessData(connections, ct);
        return connections;
    }

    private async Task CreateReference(CancellationToken ct)
    {
        ValidateName(_referenceName, "DisputePortal_Template_");
        if (Provider == DatabaseProvider.SqlServer)
        {
            await using var admin = new SqlConnection(_adminConnectionString);
            await admin.OpenAsync(ct);
            _owner = await admin.ExecuteScalarAsync<string>(new CommandDefinition("SELECT SUSER_SNAME()", cancellationToken: ct));
            if (await admin.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COUNT(*) FROM sys.databases WHERE name = @Name",
                new { Name = _referenceName },
                cancellationToken: ct)) != 0)
            {
                throw new InvalidOperationException("The generated template database already exists.");
            }

            await admin.ExecuteAsync(new CommandDefinition(
                $"CREATE DATABASE [{_referenceName}]",
                commandTimeout: CommandTimeoutSeconds,
                cancellationToken: ct));
            _created = true;
            await admin.ExecuteAsync(new CommandDefinition(
                $"ALTER DATABASE [{_referenceName}] SET AUTO_CLOSE OFF",
                commandTimeout: CommandTimeoutSeconds,
                cancellationToken: ct));
        }
        else
        {
            await using var admin = new NpgsqlConnection(_adminConnectionString);
            await admin.OpenAsync(ct);
            _owner = await admin.ExecuteScalarAsync<string>(new CommandDefinition("SELECT current_user", cancellationToken: ct));
            if (await admin.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COUNT(*) FROM pg_database WHERE datname = @Name",
                new { Name = _referenceName },
                cancellationToken: ct)) != 0)
            {
                throw new InvalidOperationException("The generated template database already exists.");
            }

            await admin.ExecuteAsync(new CommandDefinition(
                $"CREATE DATABASE \"{_referenceName}\"",
                commandTimeout: CommandTimeoutSeconds,
                cancellationToken: ct));
            _created = true;
        }
    }

    private async Task PrepareSqlBackup(CancellationToken ct)
    {
        using (var reference = new SqlConnection(_referenceConnectionString))
            SqlConnection.ClearPool(reference);
        await using var admin = new SqlConnection(_adminConnectionString);
        await admin.OpenAsync(ct);
        await AssertSqlOwnership(admin, ct);
        var directories = await admin.QuerySingleAsync<SqlDirectories>(new CommandDefinition(
            """
            SELECT CAST(SERVERPROPERTY('InstanceDefaultBackupPath') AS nvarchar(4000)) AS BackupDirectory,
                CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS nvarchar(4000)) AS DataDirectory,
                CAST(SERVERPROPERTY('InstanceDefaultLogPath') AS nvarchar(4000)) AS LogDirectory
            """,
            cancellationToken: ct));
        if (new[]
        {
            directories.BackupDirectory,
            directories.DataDirectory,
            directories.LogDirectory
        }.Any(value => string.IsNullOrWhiteSpace(value) || !Path.IsPathFullyQualified(value)))
        {
            throw new InvalidOperationException("SQL Server did not provide absolute native test backup/data directories.");
        }

        _dataDirectory = directories.DataDirectory;
        _logDirectory = directories.LogDirectory;
        _backupPath = Path.Combine(directories.BackupDirectory, _referenceName + ".bak");
        await admin.ExecuteAsync(new CommandDefinition(
            $"ALTER DATABASE [{_referenceName}] SET READ_ONLY WITH NO_WAIT",
            commandTimeout: CommandTimeoutSeconds,
            cancellationToken: ct));
        await admin.ExecuteAsync(new CommandDefinition(
            $"""
            BACKUP DATABASE [{_referenceName}]
            TO DISK = @BackupPath
            WITH COPY_ONLY, CHECKSUM, NOINIT
            """,
            new { BackupPath = _backupPath },
            commandTimeout: CommandTimeoutSeconds,
            cancellationToken: ct));
        await admin.ExecuteAsync(new CommandDefinition(
            """
            RESTORE VERIFYONLY
            FROM DISK = @BackupPath
            WITH CHECKSUM
            """,
            new { BackupPath = _backupPath },
            commandTimeout: CommandTimeoutSeconds,
            cancellationToken: ct));
        // The unique backup remains as a test artifact; no server file deletion.
    }

    private async Task CloneSql(string databaseName, CancellationToken ct)
    {
        if (_backupPath is null || _dataDirectory is null || _logDirectory is null)
        {
            throw new InvalidOperationException("The native SQL test backup is unavailable.");
        }

        var dataDirectory = _dataDirectory;
        var logDirectory = _logDirectory;
        await using var admin = new SqlConnection(_adminConnectionString);
        await admin.OpenAsync(ct);
        await AssertSqlOwnership(admin, ct);
        if (await admin.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM sys.databases WHERE name = @Name",
            new { Name = databaseName },
            cancellationToken: ct)) != 0)
        {
            throw new InvalidOperationException("The generated test database already exists; restore will not overwrite it.");
        }

        var files = (await admin.QueryAsync<SqlBackupFile>(new CommandDefinition(
            """
            RESTORE FILELISTONLY
            FROM DISK = @BackupPath
            """,
            new { BackupPath = _backupPath },
            commandTimeout: CommandTimeoutSeconds,
            cancellationToken: ct))).ToArray();
        if (files.Length < 2
            || files.Any(file => string.IsNullOrWhiteSpace(file.LogicalName)
            || file.Type is not ("D" or "L"))
            || files.Select(file => file.LogicalName).Distinct(StringComparer.Ordinal).Count() != files.Length)
        {
            throw new InvalidOperationException("The SQL test backup has an unexpected file layout.");
        }

        var moves = files.Select((file, index) =>
        {
            var directory = file.Type == "L" ? logDirectory : dataDirectory;
            var extension = file.Type == "L" ? ".ldf" : index == 0 ? ".mdf" : ".ndf";
            var physical = Path.Combine(directory, databaseName + "_" + index + extension);
            return "MOVE " + SqlLiteral(file.LogicalName) + " TO " + SqlLiteral(physical);
        });
        var restore = $"RESTORE DATABASE [{databaseName}] FROM DISK = @BackupPath WITH CHECKSUM, RECOVERY, " + string.Join(", ", moves);
        // No REPLACE: generated database and physical file names must be new.
        await admin.ExecuteAsync(new CommandDefinition(
            restore,
            new { BackupPath = _backupPath },
            commandTimeout: CommandTimeoutSeconds,
            cancellationToken: ct));
        await admin.ExecuteAsync(new CommandDefinition(
            $"""
            ALTER DATABASE [{databaseName}] SET READ_WRITE WITH NO_WAIT;
            ALTER DATABASE [{databaseName}] SET AUTO_CLOSE OFF
            """,
            commandTimeout: CommandTimeoutSeconds,
            cancellationToken: ct));
    }

    private async Task SealPostgresReference(CancellationToken ct)
    {
        using (var reference = new NpgsqlConnection(_referenceConnectionString))
            NpgsqlConnection.ClearPool(reference);
        await using var admin = new NpgsqlConnection(_adminConnectionString);
        await admin.OpenAsync(ct);
        await AssertPostgresOwnership(admin, ct);
        await admin.ExecuteAsync(new CommandDefinition(
            $"ALTER DATABASE \"{_referenceName}\" WITH ALLOW_CONNECTIONS false",
            commandTimeout: CommandTimeoutSeconds,
            cancellationToken: ct));
    }

    private async Task ClonePostgres(string databaseName, CancellationToken ct)
    {
        await using var admin = new NpgsqlConnection(_adminConnectionString);
        await admin.OpenAsync(ct);
        await AssertPostgresOwnership(admin, ct);
        if (await admin.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM pg_database WHERE datname = @Name",
            new { Name = databaseName },
            cancellationToken: ct)) != 0)
        {
            throw new InvalidOperationException("The generated test database already exists.");
        }

        await admin.ExecuteAsync(new CommandDefinition(
            $"CREATE DATABASE \"{databaseName}\" WITH TEMPLATE \"{_referenceName}\"",
            commandTimeout: CommandTimeoutSeconds,
            cancellationToken: ct));
    }

    private async Task AssertSqlOwnership(SqlConnection admin, CancellationToken ct)
    {
        var owner = await admin.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT SUSER_SNAME(owner_sid) FROM sys.databases WHERE name = @Name",
            new { Name = _referenceName },
            cancellationToken: ct));
        if (_owner is null || !string.Equals(_owner, owner, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The generated SQL test template ownership changed.");
        }
    }

    private async Task AssertPostgresOwnership(NpgsqlConnection admin, CancellationToken ct)
    {
        var owner = await admin.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT pg_get_userbyid(datdba) FROM pg_database WHERE datname = @Name",
            new { Name = _referenceName },
            cancellationToken: ct));
        if (_owner is null || !string.Equals(_owner, owner, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The generated PostgreSQL test template ownership changed.");
        }
    }

    private static async Task AssertEmptyBusinessData(IDatabaseConnectionFactory connections, CancellationToken ct)
    {
        await using var connection = await connections.OpenConnection(ct);
        var rows = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            """
            SELECT CAST((SELECT COUNT(*) FROM "identity"."users") AS bigint)
                + (SELECT COUNT(*) FROM "accounts"."accounts")
                + (SELECT COUNT(*) FROM "transactions"."transactions")
                + (SELECT COUNT(*) FROM "disputes"."disputes")
                + (SELECT COUNT(*) FROM "support"."conversations")
                + (SELECT COUNT(*) FROM "support"."messages")
            """,
            cancellationToken: ct));
        if (rows != 0)
        {
            throw new InvalidOperationException("The test template must contain migration reference data only.");
        }
    }

    private string CatalogueHash()
    {
        var directory = Path.Combine(
            AppContext.BaseDirectory,
            "Database",
            DatabaseDialect.GetDatabaseDialect(Provider).MigrationDirectoryName,
            "Initialization");
        var files = Directory.GetFiles(directory, "*.sql").OrderBy(
            Path.GetFileName,
            StringComparer.Ordinal).ToArray();
        if (files.Length == 0)
        {
            throw new InvalidOperationException("The test release has no migration catalogue.");
        }

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in files)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(Path.GetFileName(file) + "\0"));
            hash.AppendData(File.ReadAllBytes(file));
            hash.AppendData(new byte[] { 0 });
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private void EnsureCatalogueUnchanged()
    {
        if (_catalogueHash != CatalogueHash())
        {
            throw new InvalidOperationException("The test migration catalogue changed after the fresh template was prepared.");
        }
    }

    private static void ValidateName(string databaseName, string prefix)
    {
        if (!databaseName.StartsWith(prefix, StringComparison.Ordinal)
            || !Regex.IsMatch(databaseName[prefix.Length..], "\\A[a-f0-9]{32}\\z", RegexOptions.CultureInvariant))
        {
            throw new InvalidOperationException("Unexpected isolated test database name.");
        }
    }

    private static string SqlLiteral(string value) => "N'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    public async Task DisposeAsync()
    {
        if (!_created)
        {
            return;
        }

        ValidateName(_referenceName, "DisputePortal_Template_");
        if (Provider == DatabaseProvider.SqlServer)
        {
            using (var reference = new SqlConnection(_referenceConnectionString))
                SqlConnection.ClearPool(reference);
            await using var admin = new SqlConnection(_adminConnectionString);
            await admin.OpenAsync();
            await AssertSqlOwnership(admin, CancellationToken.None);
            await admin.ExecuteAsync(new CommandDefinition(
                $"""
                ALTER DATABASE [{_referenceName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{_referenceName}]
                """,
                commandTimeout: CommandTimeoutSeconds));
        }
        else
        {
            using (var reference = new NpgsqlConnection(_referenceConnectionString))
                NpgsqlConnection.ClearPool(reference);
            await using var admin = new NpgsqlConnection(_adminConnectionString);
            await admin.OpenAsync();
            await AssertPostgresOwnership(admin, CancellationToken.None);
            await admin.ExecuteAsync(new CommandDefinition($"DROP DATABASE \"{_referenceName}\"", commandTimeout: CommandTimeoutSeconds));
        }

        _created = false;
    }

    private sealed class SqlDirectories
    {
        public string BackupDirectory { get; set; } = "";
        public string DataDirectory { get; set; } = "";
        public string LogDirectory { get; set; } = "";
    }

    private sealed class SqlBackupFile
    {
        public string LogicalName { get; set; } = "";
        public string Type { get; set; } = "";
    }
}
