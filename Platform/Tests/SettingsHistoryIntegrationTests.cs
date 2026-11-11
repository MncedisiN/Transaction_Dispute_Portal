using Dapper;
using System.Data.Common;
using DisputePortal.Platform.Configuration;
using DisputePortal.Platform.Contracts;
using DisputePortal.Platform.Infrastructure;
using Xunit;
using DisputePortal.Platform.Infrastructure.Models;

namespace DisputePortal.Platform.Tests;

public partial class ServiceDomainMigrationTests
{
    [NativePostgreSqlFact]
    public async Task Settings_history_preserves_complete_snapshots_replays_and_restores_without_resurrection()
    {
        await using var fixture = await DatabaseFixture.Create("configuration");
        var db = fixture.Database;
        await db.ApplyServiceSchemaMigrations(
            Schema("configuration", db),
            ServiceMigrations.GetServiceMigrations("configuration", "PostgreSql"),
            default);
        var store = new SettingsStore(db, new NoRemoteCalls());
        await store.InitializeServiceData(default);
        var versions = new SettingsVersionStore(db);
        var before = await store.GetPlatformSettings(default);
        await using (var connection = await db.OpenServiceDatabaseConnection())
        {
            var invalidParent = await Assert.ThrowsAnyAsync<DbException>(() => connection.ExecuteAsync(
                """
                INSERT INTO configuration."settings" (
                    "policyId",
                    "publicId",
                    "version",
                    "effectiveFromUtc",
                    "publishedAtUtc",
                    "changeReason",
                    "provenance",
                    "body"
                )
                SELECT
                    id,
                    @Id,
                    version + 1,
                    CURRENT_TIMESTAMP,
                    CURRENT_TIMESTAMP,
                    'Verify settings family integrity.',
                    'Published',
                    jsonb_set(body, '{version}', to_jsonb(version + 1))
                FROM configuration."settings"
                WHERE "publicId" = @Published
                """,
                new { Id = Guid.NewGuid(), Published = before.Publication!.VersionPublicId }));
            Assert.Contains("family", invalidParent.Message, StringComparison.OrdinalIgnoreCase);
        }
        var actor = Guid.NewGuid();
        var request = new SettingsUpdate(
            before.Version,
            new global::DisputePortal.Platform.Contracts.PlatformSettings(before) { MaximumAccounts = before.MaximumAccounts + 1 },
            "Review the complete account and country configuration.")
        {
            ConcurrencyToken = before.Publication!.ConcurrencyToken
        };
        var preview = await store.Preview(request, actor, default);
        Assert.Equal(before.Version + 1, preview.Version);
        Assert.Equal(1, (await versions.History(1, 20, null, default)).Total);
        var after = await store.UpdatePlatformSettings(request, actor, default);
        var replay = await store.UpdatePlatformSettings(request, actor, default);
        Assert.Equal(after.Publication!.VersionPublicId, replay.Publication!.VersionPublicId);
        var history = await versions.History(1, 20, null, default);
        Assert.Equal(2, history.Total);
        Assert.Equal(before.MaximumAccounts, history.Items.Last().Settings.MaximumAccounts);
        Assert.Equal(actor, history.Items.First().CreatedBy);
        await using var c = await db.OpenServiceDatabaseConnection();
        await Assert.ThrowsAnyAsync<DbException>(() => c.ExecuteAsync(
            """UPDATE configuration."settings" SET body=body WHERE "publicId"=@Id""",
            new
            {
                Id = after.Publication.VersionPublicId
            }));
        var removal = new SettingsRemovalRequest
        {
            ConcurrencyToken = history.ConcurrencyToken,
            Reason = "Withdraw this configuration publication after review."
        };
        Assert.Null((await versions.SoftDeleteSettingsVersion(actor, after.Publication.VersionPublicId, removal, true, default)).Selected);
        await versions.SoftDeleteSettingsVersion(actor, after.Publication.VersionPublicId, removal, false, default);
        var unavailable = await Assert.ThrowsAsync<PlatformException>(() => store.GetPlatformSettings(default));
        Assert.Equal("settings_not_applicable", unavailable.Code);
        var deleted = (await versions.History(1, 20, null, default)).Items.First();
        await versions.SoftDeleteSettingsVersion(
            Guid.NewGuid(),
            deleted.VersionPublicId,
            new SettingsRemovalRequest
            {
                ConcurrencyToken = 0,
                Reason = "Repeat removal must retain attribution."
            },
            false,
            default);
        var again = await versions.History(1, 20, null, default);
        Assert.Equal(deleted.DeletedAtUtc, again.Items.First().DeletedAtUtc);
        Assert.Equal(actor, again.Items.First().DeletedBy);
        var restore = new SettingsUpdate(after.Version, before, "Restore the retained complete settings as a new revision.")
        {
            ConcurrencyToken = again.ConcurrencyToken,
            Operation = "Restore",
            SourceVersionPublicId = before.Publication.VersionPublicId
        };
        var restored = await store.UpdatePlatformSettings(restore, actor, default);
        Assert.Equal(after.Version + 1, restored.Version);
        Assert.Equal(before.MaximumAccounts, restored.MaximumAccounts);
        Assert.Equal(3, (await versions.History(1, 20, null, default)).Total);
    }

    [NativePostgreSqlFact]
    public async Task Captured_operation_retry_and_cancellation_survive_configuration_removal()
    {
        await using var fixture = await DatabaseFixture.Create("configuration");
        var db = fixture.Database;
        await db.ApplyServiceSchemaMigrations(
            Schema("configuration", db),
            ServiceMigrations.GetServiceMigrations("configuration", "PostgreSql"),
            default);
        var settings = new SettingsStore(db, new NoRemoteCalls());
        await settings.InitializeServiceData(default);
        var current = await settings.GetPlatformSettings(default);
        var country = CountryCurrencyPolicy.Current(current);
        var operations = new CountryOperationStore(db);
        var customer = Guid.NewGuid();
        var request = new CountryOperationRequest
        {
            RequestId = Guid.NewGuid(),
            OperationId = Guid.NewGuid(),
            SourceKind = "Payment",
            CountryCode = country.Code,
            Currency = country.DefaultCurrency,
            CustomerId = customer,
            ExpectedVersion = current.Version
        };
        var captured = await operations.Begin(request, "payments", default);
        await new SettingsVersionStore(db).SoftDeleteSettingsVersion(
            Guid.NewGuid(),
            current.Publication!.VersionPublicId,
            new SettingsRemovalRequest
            {
                ConcurrencyToken = current.Publication.ConcurrencyToken,
                Reason = "Withdraw current settings during maintenance review."
            },
            false,
            default);
        Assert.Equal(captured.OperationId, (await operations.Begin(request, "payments", default)).OperationId);
        await operations.Settle("Payment", request.RequestId, "payments", default, customer);
        Assert.NotNull((await operations.GetCountryOperation(captured.OperationId, default)).SettledAt);
        var blocked = await Assert.ThrowsAsync<PlatformException>(() => operations.Begin(
            new CountryOperationRequest
            {
                RequestId = Guid.NewGuid(),
                SourceKind = "Payment",
                CountryCode = country.Code,
                Currency = country.DefaultCurrency,
                CustomerId = customer
            },
            "payments",
            default));
        Assert.Equal("settings_not_applicable", blocked.Code);
        await operations.Settle("Payment", Guid.NewGuid(), "payments", default, customer);
    }
}
