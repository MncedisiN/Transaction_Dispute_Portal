using Microsoft.Extensions.Diagnostics.HealthChecks;
using Dapper;
using DisputePortal.Api.Interfaces.Data;
using DisputePortal.Platform.Infrastructure.Sql;

namespace DisputePortal.Api.HealthChecks;

public class DatabaseHealthCheck(IDatabaseConnectionFactory connections) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await connections.OpenConnection(cancellationToken);
            await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                """
                SELECT
                    1
                """,
                cancellationToken: cancellationToken));
            return HealthCheckResult.Healthy("Database connection is available.");
        }
        catch
        {
            return HealthCheckResult.Unhealthy("Database connection is unavailable.");
        }
    }
}
