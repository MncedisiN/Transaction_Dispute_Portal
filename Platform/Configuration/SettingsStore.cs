using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using DisputePortal.Platform.Contracts;
using DisputePortal.Platform.Infrastructure;
using DisputePortal.Platform.Configuration.Interfaces;
using DisputePortal.Platform.Infrastructure.Interfaces;
using DisputePortal.Platform.Infrastructure.Models;
using DisputePortal.Platform.Infrastructure.Sql;

namespace DisputePortal.Platform.Configuration;

public class SettingsStore(ServiceDatabase db, IServiceClient client) : ISettingsStore, IServiceInitializer
{
    public static readonly string Schema = SqlScripts.ReadSqlScript("PostgreSql/Schema/Configuration/SettingsStore/CreateSettingsSchema");

    public async Task InitializeServiceData(CancellationToken ct)
    {
        await using var c = await db.OpenServiceDatabaseConnection(ct);
        if (await c.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT
                COUNT(*)
            FROM configuration."settings"
            """,
            cancellationToken: ct)) != 0)
        {
            return;
        }

        var value = JsonSerializer.Deserialize<PlatformSettings>(
            await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "seed-settings.json"), ct),
            DomainEvent.Json)!;
        SettingsPolicy.ValidatePlatformSettings(value);
        await using var tx = await c.BeginTransactionAsync(ct);
        await c.ExecuteAsync(new CommandDefinition(
            SqlScripts.ReadSqlScript("PostgreSql/Queries/Configuration/SettingsStore/InsertSetting"),
            new { Body = JsonSerializer.Serialize(value, DomainEvent.Json) },
            transaction: tx,
            cancellationToken: ct));
        var id = await c.QuerySingleAsync<long>(new CommandDefinition(
            SqlScripts.ReadSqlScript("PostgreSql/Queries/Configuration/SettingsStore/GetSettingForInitialize"),
            transaction: tx,
            cancellationToken: ct));
        await c.ExecuteAsync(new CommandDefinition(
            SqlScripts.ReadSqlScript("PostgreSql/Queries/Configuration/SettingsStore/InsertSettingVersionByIdAndPublicIdAndBody"),
            new
            {
                Id = id,
                PublicId = Guid.NewGuid(),
                Body = JsonSerializer.Serialize(value, DomainEvent.Json)
            },
            tx,
            cancellationToken: ct));
        await tx.CommitAsync(ct);
    }

    private static PolicyVersionMetadata Metadata(SettingsRevision revision) => JsonSerializer.Deserialize<PolicyVersionMetadata>(
        JsonSerializer.Serialize<PolicyVersionMetadata>(revision, DomainEvent.Json),
        DomainEvent.Json)!;

    internal static async Task<PlatformSettings> GetPlatformSettings(DbConnection c, DbTransaction? tx, CancellationToken ct)
    {
        var revision = await SettingsVersionStore.Effective(c, tx, ct);
        return new global::DisputePortal.Platform.Contracts.PlatformSettings(revision.Settings)
        {
            Publication = Metadata(revision)
        };
    }

    public async Task<PlatformSettings> GetPlatformSettings(CancellationToken ct)
    {
        await using var c = await db.OpenServiceDatabaseConnection(ct);
        return await GetPlatformSettings(c, null, ct);
    }

    public async Task<PlatformSettings> UpdatePlatformSettings(SettingsUpdate request, Guid? actor, CancellationToken ct) => await Change(
        request,
        actor,
        false,
        ct);

    public async Task<PlatformSettings> Preview(SettingsUpdate request, Guid? actor, CancellationToken ct) => await Change(
        request,
        actor,
        true,
        ct);

    private async Task<PlatformSettings> Change(SettingsUpdate request, Guid? actor, bool preview, CancellationToken ct)
    {
        if (actor is null || actor == Guid.Empty || request.RequestId == Guid.Empty)
        {
            throw new PlatformException(400, "identity_required", "Use an authenticated actor and a publication request ID.");
        }

        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 500)
        {
            throw new PlatformException(400, "reason_required", "Give a reason for the configuration change.");
        }

        await using var c = await db.OpenServiceDatabaseConnection(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request, DomainEvent.Json))));
        var token = await c.QuerySingleAsync<long>(new CommandDefinition(
            SqlScripts.ReadSqlScript("PostgreSql/Queries/Configuration/SettingsStore/GetSettingWithConcurrencyToken"),
            transaction: tx,
            cancellationToken: ct));
        var rows = await SettingsVersionStore.Rows(c, tx, ct);
        var replay = (await SettingsVersionStore.Query(
            c,
            tx,
            SqlScripts.ReadSqlScript("PostgreSql/Queries/Configuration/SettingsStore/FilterSettingsByRequestId"),
            new
            {
                Request = request.RequestId
            },
            ct)).SingleOrDefault();
        if (replay is not null)
        {
            var priorHash = await c.QuerySingleAsync<string>(new CommandDefinition(
                """
                SELECT
                    "requestFingerprint"
                FROM configuration."settings"
                WHERE "publicId" = @Id
                """,
                new
                {
                    Id = replay.VersionPublicId
                },
                tx,
                cancellationToken: ct));
            if (priorHash != fingerprint || replay.CreatedBy != actor)
            {
                throw new PlatformException(409, "request_conflict", "This request was already used for different settings.");
            }

            await tx.CommitAsync(ct);
            return new global::DisputePortal.Platform.Contracts.PlatformSettings(replay.Settings)
            {
                Publication = Metadata(replay)
            };
        }

        if (token != request.ConcurrencyToken)
        {
            throw new PlatformException(
                409,
                "settings_changed",
                "Settings history changed. Your unsaved changes are retained; review the latest version.");
        }

        var previous = rows.First().Settings;
        var proposedSettings = request.Settings;
        if (request.Operation is "Copy" or "Restore")
        {
            proposedSettings = (await SettingsVersionStore.Query(
                c,
                tx,
                SqlScripts.ReadSqlScript("PostgreSql/Queries/Configuration/SettingsStore/FilterSettingsByPublicId"),
                new
                {
                    Source = request.SourceVersionPublicId
                },
                ct)).SingleOrDefault()?.Settings ?? throw new PlatformException(404, "source_missing", "Choose a retained source version.");
        }
        else if (request.Operation != "Create" || request.SourceVersionPublicId is not null)
        {
            throw new PlatformException(400, "invalid_operation", "Choose a valid settings publication operation.");
        }

        SettingsPolicy.ValidatePlatformSettings(proposedSettings);
        if (previous.Currencies.Any(old => !proposedSettings.Currencies.Any(next => next.Code == old.Code && next.Decimals == old.Decimals)))
        {
            throw new PlatformException(
                409,
                "currency_immutable",
                "Existing currency codes and precision must be retained for historical records.");
        }

        if (previous.Countries.Any(old => !proposedSettings.Countries.Any(next => next.Code == old.Code)))
        {
            throw new PlatformException(409, "country_immutable", "Retain existing country codes for historical accounts.");
        }

        if (previous.Countries.Any(old => proposedSettings.Countries.Single(next => next.Code == old.Code).DefaultCurrency != old.DefaultCurrency))
        {
            throw new PlatformException(
                409,
                "country_currency_immutable",
                "The configured country currency mapping cannot be changed through account settings.");
        }

        if (previous.OperatingCountry != proposedSettings.OperatingCountry)
        {
            var pending = await c.ExecuteScalarAsync<long>(new CommandDefinition(
                """
                SELECT
                    COUNT(*)
                FROM configuration."countryOperations"
                WHERE "settledAt" IS NULL
                """,
                transaction: tx,
                cancellationToken: ct));
            if (pending != 0)
            {
                throw new PlatformException(
                    409,
                    "country_change_blocked",
                    "A financial operation is still processing. Complete it before changing country.");
            }

            foreach (var service in new[]
            {
                "ledger",
                "accounts",
                "payments",
                "transactions",
                "scheduling",
                "debt",
                "identity"
            }

            )
            {
                var path = service == "identity" ? "/api/internal/country-policy/eligibility" : "/operations/country-eligibility";
                var state = await client.SendServiceRequest<CountryChangeEligibility>(service, HttpMethod.Get, path, ct: ct);
                if (!state.Allowed)
                {
                    var message = state.HasNonzeroBalances ? "Existing account balances must be settled before changing country." : state.HasReserves ? "Reserved funds must settle before changing country." : state.HasPendingRefunds ? "Pending refunds must complete before changing country." : "An account or financial operation is still processing. Complete it before changing country.";
                    throw new PlatformException(409, "country_change_blocked", message);
                }
            }
        }

        var version = checked(await c.QuerySingleAsync<int>(new CommandDefinition(
            """
            SELECT
                "lastVersion"
            FROM configuration."settings"
            WHERE code = 'Default'
            """,
            transaction: tx,
            cancellationToken: ct)) + 1);
        var at = DateTimeOffset.UtcNow;
        var value = new global::DisputePortal.Platform.Contracts.PlatformSettings(proposedSettings)
        {
            Version = version,
            Publication = null
        };
        var revision = new SettingsRevision
        {
            PolicyPublicId = rows.First().PolicyPublicId,
            VersionPublicId = Guid.NewGuid(),
            Version = version,
            ConcurrencyToken = token + 1,
            Scope = "Global",
            EffectiveFromUtc = at,
            PublishedAtUtc = at,
            CreatedAtUtc = at,
            CreatedBy = actor,
            ChangeReason = request.Reason.Trim(),
            RequestId = request.RequestId,
            SourceVersionPublicId = request.SourceVersionPublicId,
            Provenance = "Published",
            Settings = value
        };
        if (!preview)
        {
            await c.ExecuteAsync(new CommandDefinition(
                SqlScripts.ReadSqlScript("PostgreSql/Queries/Configuration/SettingsStore/InsertPublishedSettingsVersionAndAdvancePolicy"),
                new
                {
                    Id = revision.VersionPublicId,
                    Version = version,
                    At = at,
                    Actor = actor,
                    Reason = request.Reason.Trim(),
                    Request = request.RequestId,
                    Hash = fingerprint,
                    Source = request.SourceVersionPublicId,
                    Body = JsonSerializer.Serialize(value, DomainEvent.Json)
                },
                tx,
                cancellationToken: ct));
            await ServiceDatabase.InsertServiceAuditEvent(
                c,
                tx,
                actor,
                "ConfigurationUpdated",
                revision.VersionPublicId,
                request.Reason.Trim(),
                ct);
            await ServiceDatabase.InsertServiceAuditEvent(
                c,
                tx,
                actor,
                "SettingsVersionPublished",
                revision.VersionPublicId,
                JsonSerializer.Serialize(new
                {
                    Version = version,
                    request.Operation,
                    request.SourceVersionPublicId,
                    request.Reason
                }),
                ct);
            await ServiceDatabase.SaveDomainEventToOutbox(
                c,
                tx,
                DomainEvent.CreateDomainEvent(
                    "configuration",
                    "configuration.updated",
                    new
                    {
                        value.Version,
                        value.OperatingCountry,
                        Currency = CountryCurrencyPolicy.Current(value).DefaultCurrency,
                        revision.VersionPublicId
                    }),
                ct);
        }

        var resolution = PolicyVersionResolver.ResolvePublishedPolicyVersion(rows.Append(revision), "Global", at);
        await tx.CommitAsync(ct);
        return new global::DisputePortal.Platform.Contracts.PlatformSettings(value)
        {
            Publication = Metadata(resolution.Selected!)
        };
    }
}
