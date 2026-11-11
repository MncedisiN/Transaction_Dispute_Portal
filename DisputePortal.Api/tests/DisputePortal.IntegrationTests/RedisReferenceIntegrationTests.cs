using DisputePortal.Api.Caching;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DisputePortal.IntegrationTests;

public class RedisReferenceIntegrationTests
{
    [RealRedisFact]
    public async Task Independent_replicas_share_hits_and_revision_keys_reject_delayed_old_fills()
    {
        var environment = new CacheEnvironment();
        var options = DisputePortal.TestFixtures.TestRuntimeConfiguration.ApplyRedisConnectionSettings(new PortalRedisOptions
        {
            Enabled = true,
            OperationTimeoutMilliseconds = 750,
            MaximumValueBytes = 262144,
            MaximumConcurrentSourceReads = 8,
            SourceWaitMilliseconds = 250,
            FillLeaseMilliseconds = 3000,
            ConnectionString = Environment.GetEnvironmentVariable("PORTAL_TEST_REDIS"),
            ReferenceTtlSeconds = 5
        });
        await using var first = new PortalRedisConnection(options);
        await using var second = new PortalRedisConnection(options);
        using var storageOne = new RedisCache(Options.Create(new RedisCacheOptions { ConnectionMultiplexerFactory = first.GetRedisConnection }));
        using var storageTwo = new RedisCache(Options.Create(new RedisCacheOptions { ConnectionMultiplexerFactory = second.GetRedisConnection }));
        using var a = new PortalReferenceCache(storageOne, first, options, environment, NullLogger<PortalReferenceCache>.Instance);
        using var b = new PortalReferenceCache(storageTwo, second, options, environment, NullLogger<PortalReferenceCache>.Instance);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var delayed = a.GetReferenceData(
            "case-statuses",
            "race-authority",
            "1",
            async ct =>
        {
            entered.SetResult();
            await release.Task.WaitAsync(ct);
            return new Payload
            {
                Revision = "1",
                Name = "Old"
            };
        },
            x => x.Name.Length > 0,
            default,
            x => x.Revision);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        try
        {
            await b.GetReferenceData(
                "case-statuses",
                "race-authority",
                "2",
                _ => Task.FromResult(new Payload
                {
                    Revision = "2",
                    Name = "New"
                }),
                x => true,
                default,
                x => x.Revision);
        }
        finally
        {
            release.SetResult();
        }

        await delayed;
        Assert.Equal(
            "New",
            (await a.GetReferenceData<Payload>(
                "case-statuses",
                "race-authority",
                "2",
                _ => throw new InvalidOperationException("Current generation was not available as a shared cache hit."),
                x => true,
                default,
                x => x.Revision)).Name);
        var reads = 0;
        Task<Payload> Load(CancellationToken _)
        {
            Interlocked.Increment(ref reads);
            return Task.FromResult(new Payload
            {
                Revision = "1",
                Name = "First"
            });
        }

        await a.GetReferenceData("case-statuses", "isolated-authority", "1", Load, x => x.Name.Length > 0, default, x => x.Revision);
        var hit = await b.GetReferenceData(
            "case-statuses",
            "isolated-authority",
            "1",
            Load,
            x => x.Name.Length > 0,
            default,
            x => x.Revision);
        Assert.Equal("First", hit.Name);
        Assert.Equal(1, reads);
        await b.GetReferenceData(
            "case-statuses",
            "isolated-authority",
            "2",
            _ => Task.FromResult(new Payload
            {
                Revision = "2",
                Name = "Current"
            }),
            x => x.Name.Length > 0,
            default,
            x => x.Revision);
        // A delayed writer is confined to the old generation, even after a new one exists.
        await a.GetReferenceData("case-statuses", "isolated-authority", "1", Load, x => x.Name.Length > 0, default, x => x.Revision);
        var current = await a.GetReferenceData<Payload>(
            "case-statuses",
            "isolated-authority",
            "2",
            _ => throw new InvalidOperationException("A shared hit must avoid this source read."),
            x => x.Name.Length > 0,
            default,
            x => x.Revision);
        Assert.Equal("Current", current.Name);
        var isolated = await b.GetReferenceData(
            "case-statuses",
            "different-authority",
            "2",
            _ => Task.FromResult(new Payload
            {
                Revision = "2",
                Name = "Different"
            }),
            x => x.Name.Length > 0,
            default,
            x => x.Revision);
        Assert.Equal("Different", isolated.Name);
        await Task.Delay(TimeSpan.FromSeconds(5.2));
        await b.GetReferenceData("case-statuses", "isolated-authority", "1", Load, x => x.Name.Length > 0, default, x => x.Revision);
        Assert.Equal(2, reads);
        Assert.Same(await first.GetRedisConnection(), await first.GetRedisConnection());
    }

    [RealRedisFact]
    public async Task Isolated_cache_identity_cannot_read_or_write_another_environment()
    {
        var options = DisputePortal.TestFixtures.TestRuntimeConfiguration.ApplyRedisConnectionSettings(new PortalRedisOptions
        {
            Enabled = true,
            OperationTimeoutMilliseconds = 750,
            ReferenceTtlSeconds = 300,
            MaximumValueBytes = 262144,
            MaximumConcurrentSourceReads = 8,
            SourceWaitMilliseconds = 250,
            FillLeaseMilliseconds = 3000,
            ConnectionString = Environment.GetEnvironmentVariable("PORTAL_TEST_REDIS")
        });
        await using var connection = new PortalRedisConnection(options);
        var database = (await connection.GetRedisConnection()).GetDatabase();
        await database.PingAsync();
        var forbidden = "dispute:forbidden-" + Guid.NewGuid().ToString("N") + ":portal:v1:reference:test";
        var read = await Assert.ThrowsAsync<StackExchange.Redis.RedisServerException>(() => database.StringGetAsync(forbidden));
        Assert.Contains("NOPERM", read.Message);
        var write = await Assert.ThrowsAsync<StackExchange.Redis.RedisServerException>(() => database.StringSetAsync(
            forbidden,
            "blocked",
            TimeSpan.FromSeconds(2)));
        Assert.Contains("NOPERM", write.Message);
    }

    [RealRedisFact]
    public async Task Nonresponsive_network_connection_has_bounded_authoritative_fallback()
    {
        var configured = DisputePortal.TestFixtures.TestRuntimeConfiguration.ApplyRedisConnectionSettings(new PortalRedisOptions
        {
            Enabled = true,
            OperationTimeoutMilliseconds = 750,
            ReferenceTtlSeconds = 300,
            MaximumValueBytes = 262144,
            MaximumConcurrentSourceReads = 8,
            SourceWaitMilliseconds = 250,
            FillLeaseMilliseconds = 3000,
            ConnectionString = Environment.GetEnvironmentVariable("PORTAL_TEST_REDIS")
        });
        await using var healthy = new PortalRedisConnection(configured);
        await (await healthy.GetRedisConnection()).GetDatabase().PingAsync();
        // A private loopback peer accepts TCP but never answers Redis commands.
        // The real client/provider must time out; no shared server is stopped.
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start(8);
        try
        {
            var address = (System.Net.IPEndPoint)listener.LocalEndpoint;
            var settings = StackExchange.Redis.ConfigurationOptions.Parse(configured.ConnectionString!);
            settings.EndPoints.Clear();
            settings.EndPoints.Add(address.Address, address.Port);
            var options = DisputePortal.TestFixtures.TestRuntimeConfiguration.ApplyRedisConnectionSettings(new PortalRedisOptions
            {
                Enabled = true,
                ReferenceTtlSeconds = 300,
                MaximumValueBytes = 262144,
                FillLeaseMilliseconds = 3000,
                ConnectionString = settings.ToString(includePassword: true),
                OperationTimeoutMilliseconds = 200,
                MaximumConcurrentSourceReads = 1,
                SourceWaitMilliseconds = 20
            });
            await using var connection = new PortalRedisConnection(options);
            using var storage = new RedisCache(Options.Create(new RedisCacheOptions { ConnectionMultiplexerFactory = connection.GetRedisConnection }));
            using var cache = new PortalReferenceCache(
                storage,
                connection,
                options,
                new CacheEnvironment(),
                NullLogger<PortalReferenceCache>.Instance);
            var reads = 0;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var value = await cache.GetReferenceData(
                "case-statuses",
                "outage-authority",
                "1",
                _ =>
            {
                Interlocked.Increment(ref reads);
                return Task.FromResult(new Payload
                {
                    Revision = "1",
                    Name = "Authoritative"
                });
            },
                x => true,
                default,
                x => x.Revision);
            Assert.Equal("Authoritative", value.Name);
            Assert.Equal(1, reads);
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(4), "Redis network uncertainty exceeded the bounded fallback budget.");
            Assert.Same(await connection.GetRedisConnection(), await connection.GetRedisConnection());
        }
        finally
        {
            listener.Stop();
        }
    }

    private class Payload
    {
        public string Revision { get; set; } = "";
        public string Name { get; set; } = "";
    }

    private class CacheEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "test-" + Guid.NewGuid().ToString("N");
        public string ApplicationName { get; set; } = "Redis integration";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
