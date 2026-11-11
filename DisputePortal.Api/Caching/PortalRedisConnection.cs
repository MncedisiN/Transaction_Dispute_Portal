using StackExchange.Redis;

namespace DisputePortal.Api.Caching; 
public class PortalRedisConnection(PortalRedisOptions options) : IAsyncDisposable
{
    private readonly object gate = new();
    private Task<IConnectionMultiplexer>? connecting;
    private bool disposed;

    public Task<IConnectionMultiplexer> GetRedisConnection()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!options.Enabled || string.IsNullOrWhiteSpace(options.ConnectionString))
            {
                throw new InvalidOperationException("Redis is not configured.");
            }

            if (connecting is null || connecting.IsFaulted || connecting.IsCanceled)
            {
                connecting = ConnectToRedis();
            }

            return connecting;
        }
    }

    private async Task<IConnectionMultiplexer> ConnectToRedis()
    {
        var settings = ConfigurationOptions.Parse(options.ConnectionString!);
        settings.AbortOnConnectFail = false;
        settings.ConnectTimeout = options.OperationTimeoutMilliseconds;
        settings.AsyncTimeout = options.OperationTimeoutMilliseconds;
        settings.SyncTimeout = options.OperationTimeoutMilliseconds;
        settings.ConnectRetry = 0;
        settings.BacklogPolicy = BacklogPolicy.FailFast;
        settings.ReconnectRetryPolicy = new ExponentialRetry(
            options.ReconnectInitialDelayMilliseconds,
            options.ReconnectMaximumDelayMilliseconds);
        settings.SslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13;
        settings.ClientName = options.ClientName;
        settings.AllowAdmin = false;
        settings.ConfigurationChannel = "";
        settings.TieBreaker = "";
        return await ConnectionMultiplexer.ConnectAsync(settings);
    }

    public async ValueTask DisposeAsync()
    {
        Task<IConnectionMultiplexer>? pending;
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            pending = connecting;
            connecting = null;
        }
        if (pending is null)
        {
            return;
        }

        try
        {
            var connection = await pending.WaitAsync(TimeSpan.FromMilliseconds(options.DisposalTimeoutMilliseconds));
            connection.Dispose();
        }
        catch (TimeoutException)
        { 
            _ = DisposeConnectionWhenReady(pending);
        }
        catch (Exception error) when (IsExpectedConnectionFailure(error))
        {
        }
    }

    private static async Task DisposeConnectionWhenReady(Task<IConnectionMultiplexer> pending)
    {
        try
        {
            var connection = await pending;
            connection.Dispose();
        }
        catch (Exception error) when (IsExpectedConnectionFailure(error))
        {
        }
    }

    private static bool IsExpectedConnectionFailure(Exception error) =>
        error is RedisException or TimeoutException or ObjectDisposedException or ArgumentException or FormatException or OperationCanceledException;
}
