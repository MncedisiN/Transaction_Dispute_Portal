using Dapper;
using System.Data.Common;
using System.Text.Json;
using DisputePortal.Platform.Contracts;
using DisputePortal.Platform.Infrastructure;
using DisputePortal.Platform.Infrastructure.Models;
using DisputePortal.Platform.Infrastructure.Sql;

namespace DisputePortal.Platform.Configuration; 
public class SettingsVersionStore(ServiceDatabase db)
{
    internal static readonly string Select = SqlScripts.ReadSqlScript("PostgreSql/Queries/Configuration/SettingsVersionStore/GetSettingVersions");

    internal static async Task<List<SettingsRevision>> Query(
        DbConnection c,
        DbTransaction? tx,
        string suffix,
        object? parameters,
        CancellationToken ct)
    {
        var rows = (await c.QueryAsync<SettingsRevision>(new CommandDefinition(Select + suffix, parameters, tx, cancellationToken: ct))).ToList();
        foreach (var row in rows)
        {
            row.Settings = JsonSerializer.Deserialize<PlatformSettings>(row.Body!, DomainEvent.Json)!;
            row.Body = null;
        }

        return rows;
    }

    internal static Task<List<SettingsRevision>> Rows(DbConnection c, DbTransaction? tx, CancellationToken ct) => Query(
        c,
        tx,
        SqlScripts.ReadSqlScript("PostgreSql/Queries/Configuration/SettingsVersionStore/FilterSettingsVersionByLastVersion"),
        null,
        ct);

    internal static async Task<SettingsRevision> Effective(DbConnection c, DbTransaction? tx, CancellationToken ct)
    {
        var at = DateTimeOffset.UtcNow;
        return PolicyVersionResolver.ResolvePublishedPolicyVersion(await Rows(c, tx, ct), "Global", at).Selected ?? throw new PlatformException(
            409,
            "settings_not_applicable",
            "No published configuration applies. An administrator must publish a new version.");
    }

    public async Task<SettingsHistory> History(int page, int pageSize, string? search, CancellationToken ct)
    {
        if (page < 1 || pageSize is < 1 or > 100)
        {
            throw new PlatformException(400, "invalid_page", "Use a valid history page.");
        }

        await using var c = await db.OpenServiceDatabaseConnection(ct);
        var rows = await Rows(c, null, ct);
        var at = DateTimeOffset.UtcNow;
        var effective = PolicyVersionResolver.ResolvePublishedPolicyVersion(rows, "Global", at);
        var filter = SqlScripts.ReadSqlScript("PostgreSql/Queries/Configuration/SettingsVersionStore/FilterSettingsVersionByChangeReason");
        var parameters = new
        {
            Search = string.IsNullOrWhiteSpace(search) ? null : "%" + search.Trim() + "%",
            Offset = ((long)page - 1) * pageSize,
            Size = pageSize
        };
        var items = await Query(
            c,
            null,
            SqlScripts.RenderSqlScript(
            "PostgreSql/Queries/Configuration/SettingsVersionStore/GetSettingsVersionByOffsetAndSize",
            ("Filter", filter)),
            parameters,
            ct);
        var total = await c.ExecuteScalarAsync<long>(new CommandDefinition(
            SqlScripts.RenderSqlScript("PostgreSql/Queries/Configuration/SettingsVersionStore/CountSettingVersions", ("Filter", filter)),
            parameters,
            cancellationToken: ct));
        PolicyVersionResolver.ResolvePublishedPolicyVersion(rows.Concat(items), "Global", at);
        var token = await c.QuerySingleAsync<long>(new CommandDefinition(
            """
            SELECT
                "concurrencyToken"
            FROM configuration."settings"
            WHERE code = 'Default'
            """,
            cancellationToken: ct));
        return new()
        {
            Items = items,
            Total = total,
            Page = page,
            PageSize = pageSize,
            ConcurrencyToken = token,
            Effective = effective
        };
    }

    public async Task<PolicyResolution<SettingsRevision>> SoftDeleteSettingsVersion(
        Guid actor,
        Guid version,
        SettingsRemovalRequest request,
        bool preview,
        CancellationToken ct)
    {
        if (actor == Guid.Empty)
        {
            throw new PlatformException(400, "identity_required", "Use an authenticated administrator.");
        }

        await using var c = await db.OpenServiceDatabaseConnection(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        var token = await c.QuerySingleAsync<long>(new CommandDefinition(
            SqlScripts.ReadSqlScript("PostgreSql/Queries/Configuration/SettingsVersionStore/LockSettingsConcurrencyTokenForUpdate"),
            transaction: tx,
            cancellationToken: ct));
        var rows = await Rows(c, tx, ct);
        var row = (await Query(
            c,
            tx,
            SqlScripts.ReadSqlScript("PostgreSql/Queries/Configuration/SettingsVersionStore/FilterSettingsVersionByPublicId"),
            new
            {
                Version = version
            },
            ct)).SingleOrDefault() ?? throw new PlatformException(404, "version_missing", "The settings version was not found.");
        if (!row.IsDeleted)
        {
            if (token != request.ConcurrencyToken)
            {
                throw new PlatformException(409, "settings_changed", "Settings history changed. Review a fresh removal preview.");
            }

            if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length is < 10 or > 500)
            {
                throw new PlatformException(400, "reason_required", "Explain the removal in 10-500 characters.");
            }

            if (!preview)
            {
                await c.ExecuteAsync(new CommandDefinition(
                    SqlScripts.ReadSqlScript("PostgreSql/Queries/Configuration/SettingsVersionStore/SoftDeleteSettingVersions"),
                    new
                    {
                        At = DateTimeOffset.UtcNow,
                        Actor = actor,
                        Reason = request.Reason.Trim(),
                        Version = version
                    },
                    tx,
                    cancellationToken: ct));
                await ServiceDatabase.InsertServiceAuditEvent(c, tx, actor, "SettingsVersionDeleted", version, request.Reason.Trim(), ct);
                await ServiceDatabase.SaveDomainEventToOutbox(
                    c,
                    tx,
                    DomainEvent.CreateDomainEvent("configuration", "configuration.updated", new
                    {
                        row.Version,
                        Removed = true
                    }),
                    ct);
            }
        }

        row.IsDeleted = true;
        foreach (var head in rows.Where(x => x.VersionPublicId == version))
            head.IsDeleted = true;
        var result = PolicyVersionResolver.ResolvePublishedPolicyVersion(rows, "Global", DateTimeOffset.UtcNow);
        await tx.CommitAsync(ct);
        return result;
    }
}
