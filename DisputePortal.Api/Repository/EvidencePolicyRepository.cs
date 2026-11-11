using System.Text.Json;
using Dapper;
using DisputePortal.Api.Interfaces.Data;
using DisputePortal.Api.Interfaces.Repositories;
using DisputePortal.Api.Models.Configuration;

namespace DisputePortal.Api.Repository;

public class EvidencePolicyRepository(IDatabaseConnectionFactory connections) : IEvidencePolicyRepository
{
    public async Task<EvidencePolicy> GetEvidencePolicy(CancellationToken ct)
    {
        await using var connection = await connections.OpenConnection(ct);
        var policy = await connection.QuerySingleAsync<EvidencePolicy>(new CommandDefinition(
            """
            SELECT
                "version" AS Version,
                "maximumFileBytes" AS MaximumFileBytes,
                "maximumOriginalBytes" AS MaximumOriginalBytes,
                "maximumFiles" AS MaximumFiles,
                "maximumCaseBytes" AS MaximumCaseBytes,
                "maximumRequestBytes" AS MaximumRequestBytes,
                "maximumFileNameLength" AS MaximumFileNameLength,
                "minimumAccessHours" AS MinimumAccessHours,
                "maximumAccessHours" AS MaximumAccessHours,
                "defaultAccessHours" AS DefaultAccessHours,
                "accessReasonMinimumLength" AS AccessReasonMinimumLength,
                "accessReasonMaximumLength" AS AccessReasonMaximumLength
            FROM configuration."evidencePolicies"
            WHERE "code" = 'Default'
            """, cancellationToken: ct));
        var durations = await connection.QuerySingleAsync<string>(new CommandDefinition(
            """
            SELECT "accessDurations"
            FROM configuration."evidencePolicies"
            WHERE "code" = 'Default'
            """, cancellationToken: ct));
        policy.AccessDurations = JsonSerializer.Deserialize<Dictionary<string, string>>(durations)
            ?? throw new InvalidOperationException("Evidence access settings are unavailable.");
        policy.Categories = (await connection.QueryAsync<EvidenceCategoryOption>(new CommandDefinition(
            """
            SELECT
                "id" AS Id,
                "code" AS Code,
                "label" AS Label
            FROM configuration."evidenceCategories"
            WHERE "enabled" = @Enabled
            AND "deletedAt" IS NULL
            ORDER BY "sortOrder", "id"
            """, new { Enabled = true }, cancellationToken: ct))).AsList();
        var formats = await connection.QueryAsync<FormatRow>(new CommandDefinition(
            """
            SELECT
                "extension" AS Extension,
                "contentType" AS ContentType
            FROM configuration."evidenceFormats"
            WHERE "enabled" = @Enabled
            ORDER BY "sortOrder", "id"
            """, new { Enabled = true }, cancellationToken: ct));
        policy.Formats = formats.ToDictionary(row => row.Extension, row => row.ContentType, StringComparer.OrdinalIgnoreCase);
        return policy;
    }

    private class FormatRow
    {
        public string Extension { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;
    }
}
