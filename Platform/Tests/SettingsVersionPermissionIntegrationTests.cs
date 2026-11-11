using Dapper;
using DisputePortal.Platform.Configuration;
using DisputePortal.Platform.Contracts;
using DisputePortal.Platform.Infrastructure;
using Xunit;

namespace DisputePortal.Platform.Tests;

public partial class ServiceDomainMigrationTests
{
    [NativePostgreSqlFact]
    public async Task Version_permissions_follow_existing_runtime_dml_without_public_or_delete_grants()
    {
        await using var fixture = await DatabaseFixture.Create("configuration");
        var db = fixture.Database;
        var migrations = ServiceMigrations.GetServiceMigrations("configuration", "PostgreSql");
        await db.ApplyServiceSchemaMigrations(Schema("configuration", db), migrations.Take(2).ToArray(), default);
        await using var connection = await db.OpenServiceDatabaseConnection();
        // This generated no-login principal exists only for this disposable fixture.
        var role = "policy_runtime_test_" + Guid.NewGuid().ToString("N");
        await connection.ExecuteAsync($"CREATE ROLE {role} NOLOGIN; GRANT USAGE ON SCHEMA configuration TO {role}; GRANT SELECT,INSERT,UPDATE ON configuration.settings TO {role};");
        try
        {
            await db.ApplyServiceSchemaMigrations(Schema("configuration", db), migrations, default);
            await new SettingsStore(db, new NoRemoteCalls()).InitializeServiceData(default);
            await connection.ExecuteAsync($"SET ROLE {role}");
            Assert.True(await connection.ExecuteScalarAsync<bool>("""
                SELECT has_table_privilege(current_user,'configuration."settings"','SELECT')
                    AND has_table_privilege(current_user,'configuration."settings"','INSERT')
                    AND has_table_privilege(current_user,'configuration."settings"','UPDATE')
                    AND NOT has_table_privilege(current_user,'configuration."settings"','DELETE')
                """));
            Assert.Equal(
                1,
                await connection.ExecuteScalarAsync<int>("""
                SELECT COUNT(*) FROM configuration."settings" WHERE version > 0
                """));
            await using var tx = await connection.BeginTransactionAsync();
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO configuration."settings"(
                    "policyId","publicId",version,"effectiveFromUtc","publishedAtUtc","createdAtUtc",
                    "createdByPublicId","changeReason",provenance,body)
                SELECT p.id,@PublicId,p."lastVersion"+1,now(),now(),now(),@Actor,
                    'Validate runtime publication permission.','Published',
                    jsonb_set(v.body,'{version}',to_jsonb(p."lastVersion"+1))
                FROM configuration.settings p
                JOIN configuration."settings" v ON v."policyId"=p.id AND v.version=p."lastVersion"
                WHERE p.code='Default'
                """,
                new
                {
                    PublicId = Guid.NewGuid(),
                    Actor = Guid.NewGuid()
                },
                tx));
            await tx.RollbackAsync();
        }
        finally
        {
            await connection.ExecuteAsync("RESET ROLE");
            await connection.ExecuteAsync($"DROP OWNED BY {role}; DROP ROLE {role};");
        }
    }
}
