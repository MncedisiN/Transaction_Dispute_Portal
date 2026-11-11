using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using StackExchange.Redis;
using DisputePortal.Api.Models.Domain;

namespace DisputePortal.Api.Caching; 
public class PortalReferenceCache : IDisposable
{
    private readonly IDistributedCache storage;
    private readonly PortalRedisConnection redis;
    private readonly PortalRedisOptions options;
    private readonly IHostEnvironment environment;
    private readonly ILogger<PortalReferenceCache> logger;
    private readonly SemaphoreSlim? sourceReads;
    private readonly Meter meter = new("DisputePortal.Cache", "1.0.0");
    private readonly Counter<long> outcomes;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly ActivitySource Trace = new("DisputePortal.Cache");

    public PortalReferenceCache(
        IDistributedCache storage,
        PortalRedisConnection redis,
        PortalRedisOptions options,
        IHostEnvironment environment,
        ILogger<PortalReferenceCache> logger)
    {
        this.storage = storage;
        this.redis = redis;
        this.options = options;
        this.environment = environment;
        this.logger = logger;
        sourceReads = options.Enabled ? new SemaphoreSlim(options.MaximumConcurrentSourceReads) : null;
        outcomes = meter.CreateCounter<long>("portal.cache.operations");
    }

    public async Task<T> GetReferenceData<T>(
        string kind,
        string authority,
        string revision,
        Func<CancellationToken, Task<T>> load,
        Func<T, bool> valid,
        CancellationToken ct,
        Func<T, string>? revisionOf = null)
        where T : class
    {
        ct.ThrowIfCancellationRequested();
        if (!options.Enabled)
        {
            var authoritative = await load(ct);
            if (!valid(authoritative))
            {
                throw new InvalidOperationException("Authoritative reference data is invalid.");
            }

            return authoritative;
        }

        using var activity = Trace.StartActivity("reference.read");
        activity?.SetTag("cache.name", kind);
        var key = CreateReferenceCacheKey(kind, authority, revision);
        bool Current(T value) => valid(value) && (revisionOf is null || revisionOf(value) == revision);
        var hit = await TryReadCachedReferenceData<T>(key, kind, Current, ct);
        if (hit is not null)
        {
            return hit;
        }

        string? lease = null;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(options.OperationTimeoutMilliseconds);
            var database = (await redis.GetRedisConnection().WaitAsync(timeout.Token)).GetDatabase();
            var owner = Guid.NewGuid().ToString("N");
            if (await database.StringSetAsync(
                key + ":fill",
                owner,
                TimeSpan.FromMilliseconds(options.FillLeaseMilliseconds),
                When.NotExists).WaitAsync(timeout.Token))
            {
                lease = owner;
            }
            else
            {
                // Bounded cross-replica coordination. It is not a financial lock.
                for (var i = 0; i < options.FillWaitAttempts; i++)
                {
                    await Task.Delay(options.FillWaitMilliseconds, ct);
                    hit = await TryReadCachedReferenceData<T>(key, kind, Current, ct);
                    if (hit is not null)
                    {
                        return hit;
                    }
                }
            }
        }
        catch (Exception e) when (IsCacheUnavailable(e, ct))
        {
            RecordCacheOutcome(kind, "coordination-unavailable");
        }

        if (!await sourceReads!.WaitAsync(options.SourceWaitMilliseconds, ct))
        {
            RecordCacheOutcome(kind, "source-busy");
            throw new DisputePortal.Platform.Infrastructure.Models.PlatformException(
                503,
                "reference_busy",
                "Reference information is busy. Please try again shortly.");
        }

        try
        {
            hit = await TryReadCachedReferenceData<T>(key, kind, Current, ct);
            if (hit is not null)
            {
                return hit;
            }

            RecordCacheOutcome(kind, "source");
            var value = await load(ct);
            if (!valid(value))
            {
                throw new InvalidOperationException("Authoritative reference data is invalid.");
            }

            var bytes = JsonSerializer.SerializeToUtf8Bytes(new ReferenceCacheEnvelope<T> { Value = value }, Json);
            if (bytes.Length <= options.MaximumValueBytes && (revisionOf is null || revisionOf(value) == revision))
            {
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    timeout.CancelAfter(options.OperationTimeoutMilliseconds);
                    await storage.SetAsync(
                        key,
                        bytes,
                        new DistributedCacheEntryOptions
                        {
                            AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(options.ReferenceTtlSeconds * Random.Shared.Next(
                        options.TtlJitterMinimumPercent,
                        options.TtlJitterMaximumPercent + 1) / 100.0)
                        },
                        timeout.Token);
                }
                catch (Exception e) when (IsCacheUnavailable(e, ct))
                {
                    RecordCacheOutcome(kind, "write-unavailable");
                }
            }
            else
            {
                RecordCacheOutcome(kind, "oversized-source");
            }

            return value;
        }
        finally
        {
            sourceReads!.Release();
            if (lease is not null)
            {
                try
                {
                    using var timeout = new CancellationTokenSource(options.OperationTimeoutMilliseconds);
                    var database = (await redis.GetRedisConnection().WaitAsync(timeout.Token)).GetDatabase();
                    await database.ScriptEvaluateAsync(
                        "if redis.call('GET',KEYS[1]) == ARGV[1] then return redis.call('DEL',KEYS[1]) else return 0 end",
                        [key + ":fill"],
                        [lease]).WaitAsync(timeout.Token);
                }
                catch (Exception e) when (IsCacheUnavailable(e, CancellationToken.None))
                {
                    RecordCacheOutcome(kind, "release-unavailable");
                }
            }
        }
    }

    public void Dispose()
    {
        sourceReads?.Dispose();
        meter.Dispose();
    }

    private async Task<T?> TryReadCachedReferenceData<T>(string key, string kind, Func<T, bool> valid, CancellationToken ct)
        where T : class
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(options.OperationTimeoutMilliseconds);
            var bytes = await storage.GetAsync(key, timeout.Token);
            if (bytes is null)
            {
                RecordCacheOutcome(kind, "miss");
                return null;
            }

            if (bytes.Length > options.MaximumValueBytes)
            {
                RecordCacheOutcome(kind, "rejected");
                return null;
            }

            var entry = JsonSerializer.Deserialize<ReferenceCacheEnvelope<T>>(bytes, Json);
            if (entry?.RepresentationVersion != 1 || entry.Value is null || !valid(entry.Value))
            {
                RecordCacheOutcome(kind, "rejected");
                return null;
            }

            RecordCacheOutcome(kind, "hit");
            return entry.Value;
        }
        catch (JsonException)
        {
            RecordCacheOutcome(kind, "rejected");
            return null;
        }
        catch (Exception e) when (IsCacheUnavailable(e, ct))
        {
            RecordCacheOutcome(kind, "read-unavailable");
            logger.LogDebug("Shared reference cache unavailable; using bounded authoritative reads for {ReferenceKind}.", kind);
            return null;
        }
    }

    private static bool IsCacheUnavailable(Exception e, CancellationToken ct) => !ct.IsCancellationRequested
        && e is RedisException or TimeoutException or OperationCanceledException or InvalidOperationException or ArgumentException;

    private string CreateReferenceCacheKey(string kind, string authority, string revision)
    {
        if (kind is not ("case-statuses" or "transaction-presentation"))
        {
            throw new ArgumentException("Unsupported reference cache.");
        }

        var scope = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(authority))).ToLowerInvariant();
        var generation = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(revision))).ToLowerInvariant();
        return $"dispute:{environment.EnvironmentName.ToLowerInvariant()}:portal:v1:reference:{kind}:default:{scope}:{generation}";
    }


    private void RecordCacheOutcome(string kind, string outcome)
    {
        outcomes.Add(1, new KeyValuePair<string, object?>("cache.name", kind), new KeyValuePair<string, object?>("cache.outcome", outcome));
    }
}
