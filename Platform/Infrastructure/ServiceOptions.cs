namespace DisputePortal.Platform.Infrastructure;

public class ServiceOptions
{
    public int MigrationCommandTimeoutSeconds { get; set; }
    public int MigrationLockTimeoutSeconds { get; set; }
    public string Name { get; set; } = "";
    public string DatabaseProvider { get; set; } = "PostgreSql";
    public string ConnectionString { get; set; } = "";
    public string SigningKey { get; set; } = "";
    public Dictionary<string, string> PublicKeys { get; set; } = [];
    public Dictionary<string, string> Endpoints { get; set; } = [];
    public bool BrokerEnabled { get; set; }
    public string BrokerHost { get; set; } = "";
    public int BrokerPort { get; set; }
    public string BrokerVirtualHost { get; set; } = "";
    public string BrokerUser { get; set; } = "";
    public string BrokerPassword { get; set; } = "";
    public bool BrokerTls { get; set; }
    public string? BrokerServerName { get; set; }
    public int Prefetch { get; set; }
    public int MaximumAttempts { get; set; }
    public int RetryMilliseconds { get; set; }
    public int MaximumEventBytes { get; set; }
    public int BrokerPublishTimeoutSeconds { get; set; }
    public int HttpEventTimeoutSeconds { get; set; }
    public int HttpEventConcurrency { get; set; }
    public int OutboxPollMilliseconds { get; set; }
    public int RecoveryDelayMilliseconds { get; set; }
    public int BrokerRecoverySeconds { get; set; }
    public int BrokerHeartbeatSeconds { get; set; }
    public int ServiceTokenLifetimeSeconds { get; set; }
    public int ServiceTokenClockSkewSeconds { get; set; }
    public int ServiceTokenBackdateSeconds { get; set; }
    public int MaximumRetryDelaySeconds { get; set; }
    public int BrokerConnectionPollMilliseconds { get; set; }
    public int MaintenancePollMilliseconds { get; set; }
    public int OutboxLeaseSeconds { get; set; }
    public int OperatorHttpTimeoutSeconds { get; set; }
    public int CleanupTimeoutSeconds { get; set; }
    public int HstsMaxAgeDays { get; set; }
    public Dictionary<string, string[]> EventRoutes { get; set; } = [];
    public string[] Subscriptions { get; set; } = [];

    internal void ValidateMigrationConfiguration()
    {
        if (MigrationCommandTimeoutSeconds is < 1 or > 3600 || MigrationLockTimeoutSeconds is < 1 or > 3600)
        {
            throw new InvalidOperationException("Configure bounded service migration command and lock timeouts.");
        }
    }

    public void ValidateServiceConfiguration()
    {
        ValidateMigrationConfiguration();
        if (ServiceTokenLifetimeSeconds is < 10 or > 300
            || ServiceTokenClockSkewSeconds is < 0 or > 30
            || ServiceTokenBackdateSeconds is < 0 or > 30
            || MaximumRetryDelaySeconds is < 1 or > 3600
            || BrokerConnectionPollMilliseconds is < 50 or > 30000
            || MaintenancePollMilliseconds is < 50 or > 30000
            || BrokerPublishTimeoutSeconds is < 1 or > 45
            || OutboxLeaseSeconds <= Math.Max(HttpEventTimeoutSeconds, BrokerPublishTimeoutSeconds)
            || OutboxLeaseSeconds > 3600
            || OperatorHttpTimeoutSeconds is < 1 or > 900
            || CleanupTimeoutSeconds is < 1 or > 120
            || HstsMaxAgeDays is < 1 or > 730)
        {
            throw new InvalidOperationException("Configure bounded service security, recovery and lease settings.");
        }
        if (MaximumEventBytes is < 4096 or > 1048576
            || HttpEventTimeoutSeconds is < 1 or > 45
            || HttpEventConcurrency is < 1 or > 16
            || OutboxPollMilliseconds is < 50 or > 10000
            || RecoveryDelayMilliseconds is < 100 or > 30000)
        {
            throw new InvalidOperationException("Event delivery settings exceed the supported resource bounds.");
        }

        if (Endpoints.Count == 0
            || Endpoints.Keys.Any(key => !PublicKeys.ContainsKey(key))
            || Endpoints.Values.Any(value => !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)))
        {
            throw new InvalidOperationException("Configure authenticated internal service endpoints for event delivery.");
        }

        if (BrokerEnabled
            && (BrokerPort is < 1 or > ushort.MaxValue
            || Prefetch is < 1 or > ushort.MaxValue
            || MaximumAttempts is < 1 or > 16
            || RetryMilliseconds is < 1 or > 300000
            || BrokerRecoverySeconds is < 1 or > 60
            || BrokerHeartbeatSeconds is < 5 or > 300))
        {
            throw new InvalidOperationException("Service broker settings exceed the supported delivery bounds.");
        }
    }
}
