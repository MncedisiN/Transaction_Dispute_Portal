using Microsoft.Extensions.Diagnostics.HealthChecks;
using DisputePortal.Api.Interfaces.Security;

namespace DisputePortal.Api.HealthChecks;

public class EvidenceScannerHealthCheck(IEvidenceScanner scanner) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var verdict = await scanner.Scan("%PDF-1.7\nAPI readiness probe\n%%EOF"u8.ToArray(), cancellationToken);
            return verdict == "Validated" ? HealthCheckResult.Healthy("Evidence scanner is available.") : HealthCheckResult.Unhealthy("Evidence validation did not complete.");
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy("Evidence scanner is unavailable.");
        }
    }
}
