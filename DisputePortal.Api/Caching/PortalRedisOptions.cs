namespace DisputePortal.Api.Caching;

public class PortalRedisOptions
{
    public string ClientName { get; set; } = "";
    public int ReconnectInitialDelayMilliseconds { get; set; }
    public int ReconnectMaximumDelayMilliseconds { get; set; }
    public int DisposalTimeoutMilliseconds { get; set; }
    public int FillWaitAttempts { get; set; }
    public int FillWaitMilliseconds { get; set; }
    public int TtlJitterMinimumPercent { get; set; }
    public int TtlJitterMaximumPercent { get; set; } 
    public bool Enabled { get; set; }
    public string? ConnectionString { get; set; }
    public int OperationTimeoutMilliseconds { get; set; }
    public int ReferenceTtlSeconds { get; set; }
    public int MaximumValueBytes { get; set; }
    public int MaximumConcurrentSourceReads { get; set; }
    public int SourceWaitMilliseconds { get; set; }
    public int FillLeaseMilliseconds { get; set; }

    public void ValidateTransport(IHostEnvironment environment)
    {
        if (!Enabled)
        {
            return;
        }

        if (!System.Text.RegularExpressions.Regex.IsMatch(environment.EnvironmentName, "^[A-Za-z0-9_-]{1,64}$"))
        {
            throw new InvalidOperationException("Use a bounded Redis environment namespace.");
        }

        if (!environment.IsProduction() || string.IsNullOrWhiteSpace(ConnectionString))
        {
            return;
        }

        var connection = StackExchange.Redis.ConfigurationOptions.Parse(ConnectionString);
        if (!connection.Ssl
            || string.IsNullOrWhiteSpace(connection.User)
            || connection.User == "default"
            || string.IsNullOrWhiteSpace(connection.Password))
        {
            throw new InvalidOperationException("Production Redis requires TLS and a dedicated authenticated ACL user.");
        }
    }

    public void ValidateCacheResourceBounds()
    {
        if (!Enabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            throw new InvalidOperationException("Enabled Redis requires a private connection string.");
        }

        if (OperationTimeoutMilliseconds is < 100 or > 3000
            || ReferenceTtlSeconds is < 5 or > 3600
            || MaximumValueBytes is < 4096 or > 1048576
            || MaximumConcurrentSourceReads is < 1 or > 32
            || SourceWaitMilliseconds is < 1 or > 1000
            || FillLeaseMilliseconds is < 1000 or > 10000
            || string.IsNullOrWhiteSpace(ClientName) || ClientName.Length > 128
            || ReconnectInitialDelayMilliseconds is < 100 or > 30000
            || ReconnectMaximumDelayMilliseconds < ReconnectInitialDelayMilliseconds || ReconnectMaximumDelayMilliseconds > 60000
            || DisposalTimeoutMilliseconds is < 100 or > 10000
            || FillWaitAttempts is < 1 or > 10 || FillWaitMilliseconds is < 1 or > 1000
            || TtlJitterMinimumPercent is < 50 or > 100
            || TtlJitterMaximumPercent < TtlJitterMinimumPercent || TtlJitterMaximumPercent > 100)
        {
            throw new InvalidOperationException("Redis cache resource bounds are invalid.");
        }
    }
}
