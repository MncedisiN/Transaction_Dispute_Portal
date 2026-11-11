namespace DisputePortal.Api.Configuration; 
public sealed class ApiRuntimeOptions
{
    public static IReadOnlyList<string> FixedWindowPolicies { get; } = Array.AsReadOnly(new[] { "security", "auth", "sensitive", "operations-read" });
    public int EvidencePayloadWriteTimeoutSeconds { get; set; }
    public int NotificationLeaseMinutes { get; set; }
    public int RealtimeDatabaseTimeoutSeconds { get; set; }
    public int MigrationCommandTimeoutSeconds { get; set; }
    public int VerificationCodeLifetimeMinutes { get; set; }
    public int VerificationResendDelaySeconds { get; set; }
    public int LoginFlowLifetimeMinutes { get; set; }
    public string BusinessTimeZone { get; set; } = "";
    public string PasswordCheckEndpoint { get; set; } = "";
    public int RealtimeSessionCheckSeconds { get; set; }
    public int RealtimeSessionEndTimeoutSeconds { get; set; }
    public int RealtimePublishTimeoutSeconds { get; set; }
    public int RealtimeMutationTimeoutSeconds { get; set; }
    public int RealtimeTransportSendSeconds { get; set; }
    public int RealtimeCloseSeconds { get; set; }
    public int FinancialReconciliationTimeoutSeconds { get; set; }
    public int TokenClockSkewSeconds { get; set; }
    public int PushPoolLifetimeSeconds { get; set; }
    public int AssignmentIntervalSeconds { get; set; }
    public int NotificationIntervalSeconds { get; set; }
    public int AutomaticRefundIntervalSeconds { get; set; }
    public int AutomaticRefundBatchSize { get; set; }
    public int PushTimeoutSeconds { get; set; }
    public int PushConnectTimeoutSeconds { get; set; }
    public int MessagingTimeoutSeconds { get; set; }
    public int PasswordCheckTimeoutSeconds { get; set; }
    public int RealtimeHandshakeSeconds { get; set; }
    public int RealtimeKeepAliveSeconds { get; set; }
    public int RealtimeClientTimeoutSeconds { get; set; }
    public int RealtimeConnectionLimit { get; set; }
    public Dictionary<string, WindowLimit> RateLimits { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public static ApiRuntimeOptions LoadRuntimeOptions(IConfiguration configuration)
    {
        var options = configuration.GetRequiredSection("ApiRuntime").Get<ApiRuntimeOptions>() ?? throw new InvalidOperationException("ApiRuntime configuration is required.");
        options.ValidateRuntimeOptions(configuration.GetValue<bool>("PasswordSecurity:CheckBreaches"));
        _ = TimeZoneInfo.FindSystemTimeZoneById(options.BusinessTimeZone);
        return options;
    }

    public void ValidateRuntimeOptions(bool checkPasswordEndpoint = true)
    {
        int[] seconds = [EvidencePayloadWriteTimeoutSeconds, RealtimeDatabaseTimeoutSeconds, MigrationCommandTimeoutSeconds, VerificationResendDelaySeconds, RealtimeSessionCheckSeconds, RealtimeSessionEndTimeoutSeconds, RealtimePublishTimeoutSeconds, RealtimeMutationTimeoutSeconds, RealtimeTransportSendSeconds, RealtimeCloseSeconds, FinancialReconciliationTimeoutSeconds, TokenClockSkewSeconds, PushPoolLifetimeSeconds, AssignmentIntervalSeconds, NotificationIntervalSeconds, AutomaticRefundIntervalSeconds, PushTimeoutSeconds, PushConnectTimeoutSeconds, MessagingTimeoutSeconds, PasswordCheckTimeoutSeconds, RealtimeHandshakeSeconds, RealtimeKeepAliveSeconds, RealtimeClientTimeoutSeconds];
        var policies = FixedWindowPolicies;
        if (checkPasswordEndpoint
            && (!Uri.TryCreate(PasswordCheckEndpoint, UriKind.Absolute, out var endpoint)
            || endpoint.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(endpoint.UserInfo)
            || !string.IsNullOrEmpty(endpoint.Query)
            || !string.IsNullOrEmpty(endpoint.Fragment)
            || !endpoint.AbsolutePath.EndsWith('/'))
            || seconds.Any(value => value is < 1 or > 3600)
            || NotificationLeaseMinutes is < 1 or > 60
            || VerificationCodeLifetimeMinutes is < 1 or > 15
            || LoginFlowLifetimeMinutes < VerificationCodeLifetimeMinutes
            || LoginFlowLifetimeMinutes > 30
            || AutomaticRefundBatchSize is < 1 or > 100
            || RealtimeConnectionLimit is < 1 or > 100
            || RealtimeClientTimeoutSeconds <= RealtimeKeepAliveSeconds
            || PushConnectTimeoutSeconds > PushTimeoutSeconds
            || RateLimits is null
            || RateLimits.Count != policies.Count
            || policies.Any(policy => !RateLimits.TryGetValue(policy, out var limit)
            || limit is null
            || limit.PermitLimit is < 1 or > 10000
            || limit.WindowSeconds is < 1 or > 3600))
        {
            throw new InvalidOperationException("ApiRuntime contains missing or invalid operational limits.");
        }
    }

    public sealed class WindowLimit
    {
        public int PermitLimit { get; set; }
        public int WindowSeconds { get; set; }
    }
}
