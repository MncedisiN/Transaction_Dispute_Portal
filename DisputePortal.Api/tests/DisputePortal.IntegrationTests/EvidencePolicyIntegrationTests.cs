using Dapper;
using DisputePortal.Api.Models.Configuration;
using DisputePortal.Api.Models.Exceptions;
using DisputePortal.Api.Repository;
using Xunit;

namespace DisputePortal.IntegrationTests;

public sealed partial class DisputeWorkflowRepositoryTests
{
    private Task<EvidencePolicy> ReadEvidenceSettings() =>
        new EvidencePolicyRepository(_connections).GetEvidencePolicy(CancellationToken.None);

    [Fact]
    public async Task Evidence_upload_choices_and_limits_follow_database_settings()
    {
        var settings = await ReadEvidenceSettings();
        await using var connection = await _connections.OpenConnection(CancellationToken.None);
        await connection.ExecuteAsync("""
            UPDATE configuration."evidenceCategories"
            SET "enabled" = @Disabled
            WHERE "code" = @Category;

            UPDATE configuration."evidenceFormats"
            SET "enabled" = @Disabled
            WHERE "extension" = @Extension;

            UPDATE configuration."evidencePolicies"
            SET "maximumFiles" = @MaximumFiles,
                "version" = "version" + 1
            WHERE "code" = 'Default';
            """, new { Disabled = false, Category = settings.Categories[0].Code, Extension = settings.Formats.Keys.First(), MaximumFiles = settings.MaximumFiles - 1 });
        var changed = await ReadEvidenceSettings();
        Assert.DoesNotContain(settings.Categories[0].Code, changed.Categories.Select(category => category.Code));
        Assert.DoesNotContain(settings.Formats.Keys.First(), changed.Formats.Keys);
        Assert.Equal(settings.MaximumFiles - 1, changed.MaximumFiles);
        Assert.Equal(settings.Version + 1, changed.Version);
    }
}
