using System.Net.Http;
using System.Security.Claims;
using System.Text.Json;
using Dapper;
using DisputePortal.Platform.Configuration;
using DisputePortal.Platform.Contracts;
using DisputePortal.Platform.Infrastructure;
using Xunit;
using DisputePortal.Platform.Infrastructure.Interfaces;

namespace DisputePortal.Platform.Tests;

public partial class ServiceDomainMigrationTests
{
    [NativePostgreSqlFact]
    public async Task Startup_preserves_existing_administrator_configuration_without_a_silent_publication()
    {
        var ct = CancellationToken.None;
        await using var fixture = await DatabaseFixture.Create("configuration");
        var db = fixture.Database;
        await db.ApplyServiceSchemaMigrations(
            Schema("configuration", db),
            ServiceMigrations.GetServiceMigrations("configuration", "PostgreSql").Take(4).ToArray(),
            ct);
        var initial = new global::DisputePortal.Platform.Contracts.PlatformSettings(Settings)
        {
            Version = 7,
            Registration = new global::DisputePortal.Platform.Contracts.RegistrationRequirements(Settings.Registration)
            {
                RequireAddress = true
            }
        };
        await using (var connection = await db.OpenServiceDatabaseConnection(ct))
            await connection.ExecuteAsync(new CommandDefinition(
            """
                INSERT INTO configuration."settings"(code,"lastVersion","concurrencyToken") VALUES('Default',7,7);
                INSERT INTO configuration."settingVersions"("policyId","publicId",version,"effectiveFromUtc","publishedAtUtc","changeReason",provenance,body)
                SELECT id,@Id,7,now(),now(),'Observed existing administrator settings.','MigrationObservation',CAST(@Body AS jsonb) FROM configuration."settings" WHERE code='Default';
                """,
            new
            {
                Id = Guid.NewGuid(),
                Body = JsonSerializer.Serialize(initial, DomainEvent.Json)
            },
            cancellationToken: ct));
        await db.ApplyServiceSchemaMigrations(
            Schema("configuration", db),
            ServiceMigrations.GetServiceMigrations("configuration", "PostgreSql"),
            ct);
        var store = new SettingsStore(db, new NoRemoteCalls());
        var before = await store.GetPlatformSettings(ct);
        await store.InitializeServiceData(ct);
        await store.InitializeServiceData(ct);
        var after = await store.GetPlatformSettings(ct);
        Assert.Equal(
            JsonSerializer.Serialize(
                new global::DisputePortal.Platform.Contracts.PlatformSettings(before) { Publication = null },
                DomainEvent.Json),
            JsonSerializer.Serialize(
                new global::DisputePortal.Platform.Contracts.PlatformSettings(after) { Publication = null },
                DomainEvent.Json));
        Assert.True(after.Registration.RequireAddress);
        await using var c = await db.OpenServiceDatabaseConnection(ct);
        Assert.Equal(0, await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM service_audit WHERE action='ConfigurationUpdated'"));
        Assert.Equal(0, await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM service_outbox WHERE event_type='configuration.updated'"));
    }

    private sealed class NoRemoteCalls : IServiceClient
    {
        public Task<T> SendServiceRequest<T>(
            string service,
            HttpMethod method,
            string path,
            object? body = null,
            ClaimsPrincipal? actor = null,
            CancellationToken ct = default) =>
            throw new InvalidOperationException("Correcting registration metadata must not contact another service.");
    }
}
