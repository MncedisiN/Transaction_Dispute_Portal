using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace DisputePortal.Api.Caching;

public class RedisCacheHealthCheck(IDistributedCache storage, PortalRedisOptions options, IHostEnvironment environment) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(options.OperationTimeoutMilliseconds); 
            var key = $"dispute:{environment.EnvironmentName.ToLowerInvariant()}:portal:v1:reference:health:{Guid.NewGuid():N}";
            byte[] marker = [1];
            await storage.SetAsync(
                key,
                marker,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(5) },
                timeout.Token).WaitAsync(timeout.Token);
            var value = await storage.GetAsync(key, timeout.Token).WaitAsync(timeout.Token);
            await storage.RemoveAsync(key, timeout.Token).WaitAsync(timeout.Token);
            return value is [1] ? HealthCheckResult.Healthy("Shared reference cache can write, read and remove disposable entries.") : HealthCheckResult.Degraded("Redis did not return the expected disposable cache entry.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return HealthCheckResult.Degraded("Redis reference cache is unavailable. Authoritative reads remain available with concurrency limits.");
        }
    }
}
