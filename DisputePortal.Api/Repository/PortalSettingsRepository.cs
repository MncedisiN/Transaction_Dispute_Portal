using System.Text.Json;
using Dapper;
using DisputePortal.Api.Interfaces.Data;
using DisputePortal.Api.Interfaces.Repositories;
using DisputePortal.Api.Models.Configuration;
using DisputePortal.Api.Models.Exceptions;
using DisputePortal.Api.Models.Requests;
using DisputePortal.Api.Models.Responses;
using DisputePortal.Platform.Infrastructure.Sql;

namespace DisputePortal.Api.Repository;

public class PortalSettingsRepository(IDatabaseConnectionFactory factory, IDatabaseDialect databaseDialect)
    : RepositoryBase(factory, databaseDialect), IPortalSettingsRepository
{
    public async Task<PortalSettingsVersionDto> GetPortalSettings(CancellationToken ct)
    {
        await using var connection = await connections.OpenConnection(ct);
        var validation = await ReadValidation(connection, ct);
        return ToVersion(await connection.QuerySingleAsync<SettingsRow>(new CommandDefinition(
            ReadSql,
            cancellationToken: ct)), validation);
    }

    public async Task<PortalSettingsVersionDto> PublishPortalSettings(
        Guid actor,
        UpdatePortalSettingsRequest request,
        CancellationToken ct)
    {
        if (request.Settings is null || request.Reason is null || request.Reason.Trim().Length is < 5 or > 500)
        {
            throw new PortalValidationException("Supply valid portal settings and a reason for the change.");
        }

        await using var connection = await connections.OpenConnection(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await LockOperations(connection, transaction, ct);
        await RequireSupervisor(connection, transaction, actor, ct, superOnly: true);
        var validation = await ReadValidation(connection, ct, transaction);
        request.Settings.Validate(validation);
        var body = JsonSerializer.Serialize(request.Settings, JsonSerializerOptions.Web);
        if (System.Text.Encoding.UTF8.GetByteCount(body) > validation.MaximumSettingsBytes)
        {
            throw new PortalValidationException("Portal settings exceed the configured size.");
        }
        var current = await connection.QuerySingleAsync<SettingsRow>(new CommandDefinition(ReadSql, transaction: transaction, cancellationToken: ct));
        if (current.Version != request.ExpectedVersion)
        {
            throw new PortalConflictException("Portal settings changed. Reload the current version before publishing.");
        }

        var previous = ToVersion(current, validation);
        if (previous.Settings.ClientOptions.Keys.Except(request.Settings.ClientOptions.Keys, StringComparer.Ordinal).Any())
        {
            throw new PortalValidationException("Keep the existing client setting keys when publishing updated configuration.");
        }

        var id = Guid.NewGuid();
        var version = checked(current.Version + 1);
        await connection.ExecuteAsync(new CommandDefinition(
            SqlScripts.RenderSqlScript(
                "Shared/Queries/Api/PortalSettingsRepository/InsertPortalSettingsVersion",
                ("SettingsBodyValue", dialect.AuditMetadataValueExpression("@Body"))),
            new { Id = id, Version = version, Body = body, Reason = request.Reason.Trim(), Actor = actor, Now = DateTimeOffset.UtcNow },
            transaction,
            cancellationToken: ct));
        await AuditOperation(connection, transaction, actor, "PortalSettingsPublished", id, new { Version = version, PreviousVersion = current.Version, Reason = request.Reason.Trim() }, ct);
        await transaction.CommitAsync(ct);
        return new PortalSettingsVersionDto { PublicId = id, Version = version, Settings = request.Settings };
    }

    private static async Task<PortalSettingsValidation> ReadValidation(
        System.Data.Common.DbConnection connection,
        CancellationToken ct,
        System.Data.Common.DbTransaction? transaction = null)
    {
        var body = await connection.QuerySingleAsync<string>(new CommandDefinition(
            """
            SELECT "body"
            FROM configuration."portalSettings"
            WHERE "code" = 'Validation'
            ORDER BY "version" DESC
            OFFSET 0 ROWS FETCH NEXT 1 ROWS ONLY
            """, transaction: transaction, cancellationToken: ct));
        return JsonSerializer.Deserialize<PortalSettingsValidation>(body, JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException("Portal validation settings are unavailable.");
    }

    private static PortalSettingsVersionDto ToVersion(SettingsRow row, PortalSettingsValidation validation)
    {
        var settings = JsonSerializer.Deserialize<PortalReferenceSettings>(row.Body, JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException("Portal settings are unavailable.");
        settings.Validate(validation);
        return new PortalSettingsVersionDto { PublicId = row.PublicId, Version = row.Version, Settings = settings };
    }

    private const string ReadSql = """
        SELECT
            "publicId" AS PublicId,
            "version" AS Version,
            "body" AS Body
        FROM "configuration"."portalSettings"
        WHERE "code" = 'Interface'
        ORDER BY "version" DESC
        OFFSET 0 ROWS FETCH NEXT 1 ROWS ONLY
        """;

    private class SettingsRow
    {
        public Guid PublicId { get; set; }
        public int Version { get; set; }
        public string Body { get; set; } = string.Empty;
    }
}
