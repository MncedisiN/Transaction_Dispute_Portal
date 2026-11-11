using System.Data;
using System.Data.Common;
using System.Text.Json;
using Dapper;
using DisputePortal.Platform.Contracts;
using DisputePortal.Platform.Infrastructure.Models;
using DisputePortal.Platform.Infrastructure.Sql;

namespace DisputePortal.Platform.Infrastructure;

public class ServiceDatabase(ServiceOptions options)
{
    public TimeSpan CleanupTimeout => TimeSpan.FromSeconds(options.CleanupTimeoutSeconds);

    public RelationalDialect Dialect { get; } = RelationalDialect.GetDatabaseDialect(options.DatabaseProvider);

    public async Task<DbConnection> OpenServiceDatabaseConnection(CancellationToken ct = default)
    {
        var c = Dialect.CreateDatabaseConnection(options.ConnectionString);
        try
        {
            await c.OpenAsync(ct);
            return c;
        }
        catch
        {
            await c.DisposeAsync();
            throw;
        }
    }

    private static string Hash(string schema) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(schema)));

    public Task ValidateSchema(string schema, CancellationToken ct) => ValidateSchema(schema, [], ct);

    public async Task ValidateSchema(string schema, IReadOnlyList<ServiceMigration> migrations, CancellationToken ct)
    {
        options.ValidateMigrationConfiguration();
        await using var c = await OpenServiceDatabaseConnection(ct);
        if (await c.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT
                COUNT(*)
            FROM service_migrations
            WHERE version = @Hash
            """,
            new
            {
                Hash = Hash(schema)
            },
            cancellationToken: ct, commandTimeout: options.MigrationCommandTimeoutSeconds)) != 1)
        {
            throw new InvalidOperationException("Apply service migrations with the migration identity before startup.");
        }

        if (migrations.Count > 0)
        {
            await VerifyVersions(c, null, migrations, true, ct);
        }
    }

    public Task ApplyServiceSchemaMigrations(string schema, CancellationToken ct = default) =>
        ApplyServiceSchemaMigrations(schema, [], ct);

    public async Task ApplyServiceSchemaMigrations(
        string schema,
        IReadOnlyList<ServiceMigration> migrations,
        CancellationToken ct = default)
    {
        options.ValidateMigrationConfiguration();
        await using var c = await OpenServiceDatabaseConnection(ct);
        await c.ExecuteAsync(new CommandDefinition(
            Dialect.AcquireMigrationLock,
            commandTimeout: options.MigrationLockTimeoutSeconds,
            cancellationToken: ct));
        try
        {
            await c.ExecuteAsync(new CommandDefinition(
                Dialect.BaseSchema,
                cancellationToken: ct,
                commandTimeout: options.MigrationCommandTimeoutSeconds));
            await using var tx = await c.BeginTransactionAsync(ct);
            Dictionary<string, string> applied = [];
            if (migrations.Count > 0)
            {
                await c.ExecuteAsync(new CommandDefinition(
                    VersionSchema,
                    transaction: tx,
                    cancellationToken: ct,
                    commandTimeout: options.MigrationCommandTimeoutSeconds));
                applied = await VerifyVersions(c, tx, migrations, false, ct);
            }

            var hash = Hash(schema);
            if (await c.ExecuteScalarAsync<int>(new CommandDefinition(
                """
                SELECT
                    COUNT(*)
                FROM service_migrations
                WHERE version = @Hash
                """,
                new
                {
                    Hash = hash
                },
                tx,
                cancellationToken: ct, commandTimeout: options.MigrationCommandTimeoutSeconds)) == 0)
            {
                if (applied.Count > 0)
                {
                    throw new InvalidOperationException("The release changed immutable applied service migration history.");
                }

                await ExecuteBatches(c, tx, schema, ct);
                await c.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO service_migrations(
                        version
                    )
                    VALUES (
                        @Hash
                    )
                    """,
                    new
                    {
                        Hash = hash
                    },
                    tx,
                    cancellationToken: ct, commandTimeout: options.MigrationCommandTimeoutSeconds));
            }

            if (migrations.Count > 0)
            {
                foreach (var migration in migrations.OrderBy(value => value.Version, StringComparer.Ordinal))
                {
                    if (applied.ContainsKey(migration.Version))
                    {
                        continue;
                    }

                    await ExecuteBatches(c, tx, migration.Sql, ct);
                    await c.ExecuteAsync(new CommandDefinition(
                        """
                        INSERT INTO configuration."serviceSchemaVersions"(
                            "serviceName",
                            version,
                            checksum
                        )
                        VALUES (
                            @Service,
                            @Version,
                            @Checksum
                        )
                        """,
                        new
                        {
                            Service = options.Name,
                            migration.Version,
                            migration.Checksum
                        },
                        tx,
                        cancellationToken: ct, commandTimeout: options.MigrationCommandTimeoutSeconds));
                }
            }

            await tx.CommitAsync(ct);
        }
        finally
        {
            await c.ExecuteAsync(new CommandDefinition(
                Dialect.ReleaseMigrationLock,
                cancellationToken: CancellationToken.None,
                commandTimeout: options.MigrationCommandTimeoutSeconds));
        }
    }

    private async Task ExecuteBatches(DbConnection connection, DbTransaction transaction, string sql, CancellationToken ct)
    {
        foreach (var batch in System.Text.RegularExpressions.Regex.Split(sql, @"(?im)^\s*GO\s*$").Where(value => !string.IsNullOrWhiteSpace(value)))
            await connection.ExecuteAsync(new CommandDefinition(
                batch,
                transaction: transaction,
                commandTimeout: options.MigrationCommandTimeoutSeconds,
                cancellationToken: ct));
    }

    private async Task<Dictionary<string, string>> VerifyVersions(
        DbConnection connection,
        DbTransaction? transaction,
        IReadOnlyList<ServiceMigration> expected,
        bool requireAll,
        CancellationToken ct)
    {
        if (expected.Select(value => value.Version).Distinct(StringComparer.Ordinal).Count() != expected.Count)
        {
            throw new InvalidOperationException("The service migration catalogue contains a duplicate version.");
        }

        var rows = await connection.QueryAsync<MigrationVersion>(new CommandDefinition(
            """
            SELECT
                version AS Version,
                checksum AS Checksum
            FROM configuration."serviceSchemaVersions"
            WHERE "serviceName" = @Service
            """,
            new
            {
                Service = options.Name
            },
            transaction,
            cancellationToken: ct, commandTimeout: options.MigrationCommandTimeoutSeconds));
        var applied = rows.ToDictionary(value => value.Version, value => value.Checksum, StringComparer.Ordinal);
        var catalogue = expected.ToDictionary(value => value.Version, StringComparer.Ordinal);
        if (applied.Any(pair => !catalogue.TryGetValue(pair.Key, out var migration)
            || migration.Checksum != pair.Value)
            || requireAll
            && expected.Any(value => !applied.ContainsKey(value.Version)))
        {
            throw new InvalidOperationException("The service release does not match the applied migration catalogue.");
        }

        return applied;
    }

    private string VersionSchema => Dialect is PostgreSqlDialect ? SqlScripts.ReadSqlScript("Shared/Schema/Infrastructure/ServiceDatabase/CreateServiceDatabaseSchema") : SqlScripts.ReadSqlScript("SqlServer/Schema/Infrastructure/ServiceDatabase/CreateServiceDatabaseSchema");

    public static Task SaveDomainEventToOutbox(DbConnection c, DbTransaction tx, DomainEvent value, CancellationToken ct)
    {
        value.ValidateEventEnvelope();
        var dialect = c is Npgsql.NpgsqlConnection ? (RelationalDialect)new PostgreSqlDialect() : new SqlServerDialect();
        return c.ExecuteAsync(new CommandDefinition(
            dialect.InsertOutbox,
            new
            {
                Id = value.EventId,
                Type = value.EventType,
                Body = JsonSerializer.Serialize(value, DomainEvent.Json),
                Correlation = value.CorrelationId
            },
            tx,
            cancellationToken: ct));
    }

    public static Task InsertServiceAuditEvent(
        DbConnection c,
        DbTransaction tx,
        Guid? actor,
        string action,
        Guid entity,
        string detail,
        CancellationToken ct) => c.ExecuteAsync(new CommandDefinition(
        """
        INSERT INTO service_audit(
            actor,
            action,
            entity_id,
            detail
        )
        VALUES (
            @Actor,
            @Action,
            @Entity,
            @Detail
        )
        """,
        new
        {
            Actor = actor,
            Action = action,
            Entity = entity,
            Detail = detail
        },
        tx,
        cancellationToken: ct));

    public async Task ProcessEventWithInboxDeduplication(
        DomainEvent value,
        Func<DbConnection, DbTransaction, Task> handler,
        CancellationToken ct,
        string? consumerName = null)
    {
        value.ValidateEventEnvelope();
        await using var c = await OpenServiceDatabaseConnection(ct);
        await using var tx = await c.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        if (await c.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            Dialect.FindProcessed,
            new
            {
                Id = value.EventId,
                Consumer = consumerName ?? options.Name
            },
            tx,
            cancellationToken: ct)) is not null)
        {
            await tx.CommitAsync(ct);
            return;
        }

        await handler(c, tx);
        await c.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO service_inbox(
                event_id,
                consumer_name,
                event_type
            )
            VALUES (
                @Id,
                @Consumer,
                @Type
            )
            """,
            new
            {
                Id = value.EventId,
                Consumer = consumerName ?? options.Name,
                Type = value.EventType
            },
            tx,
            cancellationToken: ct));
        await tx.CommitAsync(ct);
    }

    private class MigrationVersion
    {
        public string Version { get; set; } = "";
        public string Checksum { get; set; } = "";
    }
}
