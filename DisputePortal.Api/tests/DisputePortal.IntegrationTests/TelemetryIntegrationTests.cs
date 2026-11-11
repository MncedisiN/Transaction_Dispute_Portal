using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Dapper;
using DisputePortal.Api.Authorization;
using DisputePortal.Api.Controllers;
using DisputePortal.Api.Middleware;
using DisputePortal.Api.Observability;
using DisputePortal.Api.Repository.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Data.SqlClient;
using Npgsql;
using Xunit;
using DisputePortal.Api.Models.Domain;
using DisputePortal.Api.Interfaces.Observability;
using DisputePortal.Api.Interfaces.Data;

namespace DisputePortal.IntegrationTests;

[CollectionDefinition("Telemetry integration", DisableParallelization = true)]
public sealed class TelemetryIntegrationCollection;
[Collection("Telemetry integration")]
public sealed class TelemetryIntegrationTests : IAsyncLifetime
{
    private readonly string name = "PortalTelemetry_Test_" + Guid.NewGuid().ToString("N");
    private IDatabaseConnectionFactory connections = null!;
    private DatabaseProvider provider;
    private string admin = "";

    public async Task InitializeAsync()
    {
        var sql = Environment.GetEnvironmentVariable("PORTAL_TEST_SQLSERVER");
        provider = string.IsNullOrWhiteSpace(sql) ? DatabaseProvider.PostgreSql : DatabaseProvider.SqlServer;
        if (provider == DatabaseProvider.SqlServer)
        {
            var settings = new SqlConnectionStringBuilder(sql)
            {
                InitialCatalog = name,
                CommandTimeout = 120
            };
            connections = new DatabaseConnectionFactory(provider, settings.ConnectionString);
            admin = new SqlConnectionStringBuilder(sql)
            {
                InitialCatalog = "master"
            }.ConnectionString;
            await connections.EnsureDatabaseExists(default);
        }
        else
        {
            var pg = Environment.GetEnvironmentVariable("PORTAL_TEST_POSTGRESQL") ?? Environment.GetEnvironmentVariable("PORTAL_TEST_POSTGRES") ?? throw new InvalidOperationException("Set an approved isolated SQL Server or PostgreSQL test connection.");
            admin = new NpgsqlConnectionStringBuilder(pg)
            {
                Database = "postgres"
            }.ConnectionString;
            await using var c = new NpgsqlConnection(admin);
            await c.OpenAsync();
            connections = new DatabaseConnectionFactory(
                provider,
                new NpgsqlConnectionStringBuilder(pg) { Database = name }.ConnectionString);
            await connections.EnsureDatabaseExists(default);
        }

        // Apply the same complete migration chain used by the API.
        await new DatabaseInitializer(
            connections,
            DatabaseDialect.GetDatabaseDialect(provider),
            DisputePortal.TestFixtures.TestRuntimeConfiguration.Read()).InitializeDatabase(default);
    }

    public async Task DisposeAsync()
    {
        if (!name.StartsWith("PortalTelemetry_Test_", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Only isolated test databases can be dropped.");
        }

        if (provider == DatabaseProvider.SqlServer)
        {
            SqlConnection.ClearAllPools();
            await using var c = new SqlConnection(admin);
            await c.OpenAsync();
            await c.ExecuteAsync("ALTER DATABASE [" + name + "] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [" + name + "];");
        }
        else
        {
            NpgsqlConnection.ClearAllPools();
            await using var c = new NpgsqlConnection(admin);
            await c.OpenAsync();
            await c.ExecuteAsync("DROP DATABASE \"" + name + "\" WITH (FORCE)");
        }
    }

    private ApiMetricStore Store() => new(connections, DatabaseDialect.GetDatabaseDialect(provider), new TelemetryOptions { DefaultLookbackHours = 24, FlushSeconds = 15, MaximumSeries = 4096, BatchSize = 256, RetentionDays = 30, StorageTimeoutSeconds = 5 });

    [Fact]
    public async Task Aggregates_are_transactional_idempotent_readable_and_pruned()
    {
        var store = Store();
        var now = DateTimeOffset.UtcNow;
        var key = new MetricKey
        {
            BucketStart = now.AddMinutes(-1),
            Method = "GET",
            Route = "api/accounts/{id:guid}",
            StatusCode = 200
        };
        var batch = new MetricBatch
        {
            Id = Guid.NewGuid(),
            Summaries = [new global::DisputePortal.Api.Observability.MetricSummary
            {
                Key = key,
                RequestCount = 3,
                FailureCount = 0,
                ClientErrorCount = 0,
                TotalDurationMs = 60,
                MinimumDurationMs = 10,
                MaximumDurationMs = 30
            }

            ]
        };
        await store.Persist(batch, default);
        await store.Persist(batch, default);
        await store.Persist(
            new global::DisputePortal.Api.Observability.MetricBatch
            {
                Id = Guid.NewGuid(),
                Summaries = [new global::DisputePortal.Api.Observability.MetricSummary {
                    Key = key,
                    RequestCount = 2,
                    FailureCount = 0,
                    ClientErrorCount = 0,
                    TotalDurationMs = 30,
                    MinimumDurationMs = 5,
                    MaximumDurationMs = 25
                }]
            },
            default);
        var result = await store.Overview(now.AddHours(-1), now, 100, default);
        Assert.Equal(5, result.RequestCount);
        var row = Assert.Single(result.Endpoints);
        Assert.Equal(18, row.AverageDurationMs);
        Assert.Equal(5, row.MinimumDurationMs);
        Assert.Equal(30, row.MaximumDurationMs);
        await using (var c = await connections.OpenConnection(default))
            Assert.Equal(1, await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM monitoring.\"apiMetricSummaries\""));
        await store.Prune(now, default);
        Assert.Empty((await store.Overview(now.AddHours(-1), now, 100, default)).Endpoints);
    }

    [Fact]
    public async Task Real_HTTP_requests_are_collected_persisted_and_secured_without_sensitive_dimensions()
    {
        await using var app = await Start();
        using var http = Client(app);
        var secret = Guid.NewGuid().ToString();
        Assert.Equal(HttpStatusCode.NoContent, (await http.GetAsync("sample/" + secret + "?token=do-not-store-this")).StatusCode);
        Assert.Equal(HttpStatusCode.InternalServerError, (await http.GetAsync("failure")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync("missing-account-" + secret)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("api/admin/monitoring/overview")).StatusCode);
        http.DefaultRequestHeaders.Add("X-Test-Role", "customer");
        Assert.Equal(HttpStatusCode.Forbidden, (await http.GetAsync("api/admin/monitoring/overview")).StatusCode);
        http.DefaultRequestHeaders.Remove("X-Test-Role");
        http.DefaultRequestHeaders.Add("X-Test-Role", "super_admin");
        var store = app.Services.GetRequiredService<IApiMetricStore>();
        MonitoringOverview overview = null!;
        for (var i = 0; i < 100; i++)
        {
            overview = await store.Overview(DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow, 100, default);
            if (overview.RequestCount >= 5)
            {
                break;
            }

            await Task.Delay(100);
        }

        Assert.True(overview.RequestCount >= 5);
        Assert.True(overview.FailureCount >= 1);
        Assert.True(overview.ClientErrorCount >= 3);
        Assert.True(
            overview.Endpoints.Any(r => r.Route == "/sample/{id:guid}" && r.StatusCode == 204 && r.Method == "GET"),
            "Collected routes: " + string.Join(",", overview.Endpoints.Select(r => r.Route + ":" + r.StatusCode)));
        var response = await http.GetAsync("api/admin/monitoring/overview");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        Assert.Contains("sample/{id:guid}", text);
        Assert.DoesNotContain(secret, text);
        Assert.DoesNotContain("do-not-store-this", text);
        await using var db = await connections.OpenConnection(default);
        var routes = await db.QueryAsync<string>("SELECT route FROM monitoring.\"apiMetricSummaries\"");
        Assert.DoesNotContain(routes, r => r.Contains(secret) || r.Contains('?'));
    }

    [Fact]
    public async Task Noncritical_storage_failure_does_not_interrupt_normal_HTTP_requests()
    {
        await using var app = await Start(new UnavailableStore());
        using var http = Client(app);
        Assert.Equal(HttpStatusCode.NoContent, (await http.GetAsync("sample/" + Guid.NewGuid())).StatusCode);
        await Task.Delay(1200); // let the real persistence worker attempt its first batch
        Assert.Equal(HttpStatusCode.NoContent, (await http.GetAsync("sample/" + Guid.NewGuid())).StatusCode);
        http.DefaultRequestHeaders.Add("X-Test-Role", "super_admin");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await http.GetAsync("api/admin/monitoring/overview")).StatusCode);
    }

    private async Task<WebApplication> Start(IApiMetricStore? replacement = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Configuration.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), false, false);
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Monitoring:FlushSeconds"] = "1" });
        builder.Services.AddSingleton(connections);
        builder.Services.AddSingleton(DatabaseDialect.GetDatabaseDialect(provider));
        builder.Services.AddPortalMonitoring(builder.Configuration);
        if (replacement is not null)
        {
            builder.Services.AddSingleton(replacement);
        }

        builder.Services.AddAuthentication("test").AddScheme<AuthenticationSchemeOptions, TestAuthentication>("test", _ =>
        {
        });
        builder.Services.AddPortalAuthorization();
        builder.Services.AddRateLimiter(options => options.AddPolicy(
            "operations-read",
            _ => System.Threading.RateLimiting.RateLimitPartition.GetNoLimiter("test")));
        builder.Services.AddControllers().AddApplicationPart(typeof(MonitoringController).Assembly);
        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<PortalExceptionHandler>();
        var app = builder.Build();
        app.UseExceptionHandler();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();
        app.MapGet("/sample/{id:guid}", () => Results.NoContent());
        app.MapGet("/failure", (HttpContext _) => throw new InvalidOperationException("password=never-export-this"));
        app.MapControllers();
        await app.StartAsync();
        return app;
    }

    private static HttpClient Client(WebApplication app) => new()
    {
        BaseAddress = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single())
    };

    private sealed class TestAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(
        options,
        logger,
        encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var role = Request.Headers["X-Test-Role"].ToString();
            return Task.FromResult(string.IsNullOrEmpty(role) ? AuthenticateResult.NoResult() : AuthenticateResult.Success(new AuthenticationTicket(
                new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, role)], Scheme.Name)),
                Scheme.Name)));
        }
    }

    private sealed class UnavailableStore : IApiMetricStore
    {
        public Task Persist(MetricBatch batch, CancellationToken ct) => throw new IOException("private connection details");

        public Task<MonitoringOverview> Overview(DateTimeOffset from, DateTimeOffset to, int limit, CancellationToken ct) =>
            throw new IOException("private connection details");

        public Task Prune(DateTimeOffset before, CancellationToken ct) => Task.CompletedTask;
    }
}
