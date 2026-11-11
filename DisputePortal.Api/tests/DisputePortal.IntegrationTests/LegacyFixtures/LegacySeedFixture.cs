using Dapper;
using DisputePortal.Api.Repository.Data;

namespace DisputePortal.IntegrationTests;

public sealed partial class DisputeWorkflowRepositoryTests
{
    private async Task SeedLegacyFixtures(CancellationToken ct)
    {
        var sql = await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "Repository", "Seeds", DatabaseDialect.GetDatabaseDialect(_provider).SeedFileName),
            ct);
        await using var connection = await _connections.OpenConnection(ct);
        await connection.ExecuteAsync(new CommandDefinition(sql, commandTimeout: 120, cancellationToken: ct));
    }
}
