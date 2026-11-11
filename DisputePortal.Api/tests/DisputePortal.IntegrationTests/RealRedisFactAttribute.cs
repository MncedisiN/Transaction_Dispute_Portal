using Xunit;

namespace DisputePortal.IntegrationTests;

public class RealRedisFactAttribute : FactAttribute
{
    public RealRedisFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PORTAL_TEST_REDIS")))
        {
            Skip = "Requires the isolated real Redis instance. Run tools/setup-redis-wsl.ps1 after the required Windows restart, then tools/test-redis.ps1.";
        }
    }
}
