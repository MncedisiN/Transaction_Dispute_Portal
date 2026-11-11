using DisputePortal.Api.Models.Domain;
using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using Dapper;
using DisputePortal.Api.Controllers;
using DisputePortal.Api.Middleware;
using DisputePortal.Api.Repository;
using DisputePortal.Api.Repository.Data;
using DisputePortal.Api.Service.Disputes;
using DisputePortal.Platform.Contracts;
using DisputePortal.Platform.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace DisputePortal.IntegrationTests;

public sealed partial class DisputeWorkflowRepositoryTests
{
    private DatabaseConfiguration CountryProbeDatabase() =>
        DatabaseConfiguration.ResolveDatabaseConfiguration(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Database:Provider"] = _provider.ToString(),
        ["ConnectionStrings:Portal"] = "fixture-provider-metadata"
    }).Build());

    private CountryPolicyEligibilityReader CountryProbe() =>
        new(_connections, CountryProbeDatabase(), new ImportedCountryAccountsClient());

    private async Task<Guid> QueueCountryProbeCommand(string kind, CancellationToken ct)
    {
        var created = await _repository.CreateDispute(
            Customer,
            new CreateDisputeCommand
            {
                TransactionId = Transaction,
                Category = "Other",
                Reason = "Other",
                Description = "Review this payment before changing country."
            },
            "country-probe-" + Guid.NewGuid().ToString("N"),
            ct);
        var command = Guid.NewGuid();
        await using var connection = await _connections.OpenConnection(ct);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO "disputes"."financialCommands" (
                "publicId",
                "requestId",
                "disputeId",
                "originalTransactionId",
                "accountId",
                "customerId",
                "kind",
                "amount",
                "currencyCode",
                "reason",
                "expectedVersion",
                "correlation",
                "state")
            SELECT @Command,
                @Request,
                d."id",
                t."id",
                t."accountId",
                d."customerId",
                @Kind,
                t."amount",
                t."currencyCode",
                'Country eligibility isolated pending-work fixture.',
                d."version",
                @Correlation,
                'Queued'
            FROM "disputes"."disputes" d
            JOIN "transactions"."transactions" t
                ON t."id" = d."transactionId"
            WHERE d."publicId" = @Dispute
            """,
            new
            {
                Command = command,
                Request = Guid.NewGuid(),
                Correlation = Guid.NewGuid(),
                Kind = kind,
                Dispute = created.Id
            },
            cancellationToken: ct));
        return command;
    }

    [Theory]
    [InlineData("Refund")]
    [InlineData("Recall")]
    public async Task Country_policy_probe_reads_actual_queued_refunds_and_recalls_on_either_provider(string kind)
    {
        var ct = CancellationToken.None;
        var reader = CountryProbe();
        var clear = await reader.GetCountryChangeBlockers(ct);
        Assert.False(clear.HasPendingRefunds);
        Assert.False(clear.HasUnfinishedOperations);
        Assert.Null(clear.Blocker);
        await QueueCountryProbeCommand(kind, ct);
        var pending = await reader.GetCountryChangeBlockers(ct);
        Assert.True(pending.HasPendingRefunds);
        Assert.True(pending.HasUnfinishedOperations);
        Assert.Contains("refund or recall", pending.Blocker);
    }

    [Fact]
    public async Task Country_policy_probe_cannot_dirty_read_an_uncommitted_completion_and_does_not_wait_for_the_operation_fence()
    {
        var ct = CancellationToken.None;
        var command = await QueueCountryProbeCommand("Refund", ct);
        await using var connection = await _connections.OpenConnection(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(
            RelationalDialect.GetDatabaseDialect(_provider.ToString()).OperationsLockSql,
            transaction: transaction,
            cancellationToken: ct));
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE "disputes"."financialCommands"
            SET "state" = 'Posted',
                "completedAt" = @Now
            WHERE "publicId" = @Id
            """,
            new { Id = command, Now = DateTimeOffset.UtcNow },
            transaction,
            cancellationToken: ct));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var clock = Stopwatch.StartNew();
        var busy = await CountryProbe().GetCountryChangeBlockers(deadline.Token);
        Assert.True(busy.HasUnfinishedOperations);
        Assert.NotNull(busy.Blocker);
        Assert.True(
            clock.Elapsed < TimeSpan.FromSeconds(3),
            "Country eligibility must fail closed without waiting for the writer that holds the configuration dependency.");
        await transaction.RollbackAsync(ct);
        var stillPending = await CountryProbe().GetCountryChangeBlockers(ct);
        Assert.True(stillPending.HasPendingRefunds);
        Assert.True(stillPending.HasUnfinishedOperations);
    }

    [Theory]
    [InlineData("dispute.financial.requested", true)]
    [InlineData("notification.requested", false)]
    public async Task Country_policy_probe_counts_only_pending_financial_outbox_delivery(string eventType, bool blocked)
    {
        var ct = CancellationToken.None;
        var id = Guid.NewGuid();
        await using var connection = await _connections.OpenConnection(ct);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO "service_outbox" (
                event_id,
                event_type,
                event_version,
                body,
                correlation_id,
                state,
                next_attempt_at)
            VALUES (@Id, @Type, 1, '{}', @Correlation, 'Pending', @Now)
            """,
            new
            {
                Id = id,
                Type = eventType,
                Correlation = Guid.NewGuid(),
                Now = DateTimeOffset.UtcNow
            },
            cancellationToken: ct));
        var result = await CountryProbe().GetCountryChangeBlockers(ct);
        Assert.False(result.HasPendingRefunds);
        Assert.Equal(blocked, result.HasUnfinishedOperations);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE "service_outbox"
            SET state = 'Published',
                published_at = @Now
            WHERE event_id = @Id
            """,
            new { Id = id, Now = DateTimeOffset.UtcNow },
            cancellationToken: ct));
        Assert.False((await CountryProbe().GetCountryChangeBlockers(ct)).HasUnfinishedOperations);
    }

    [Fact]
    public async Task Country_policy_HTTP_probe_requires_a_configuration_signature_and_rejects_admin_cookies_and_other_service_keys()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var ct = deadline.Token;
        using var configurationKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var paymentsKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var validator = DisputePortal.TestFixtures.TestRuntimeConfiguration.ApplyServiceMigrationSettings(new ServiceOptions
        {
            Name = "identity",
            PublicKeys = new Dictionary<string, string>
            {
                ["configuration"] = configurationKey.ExportSubjectPublicKeyInfoPem(),
                ["payments"] = paymentsKey.ExportSubjectPublicKeyInfoPem()
            }
        });
        var configuration = DisputePortal.TestFixtures.TestRuntimeConfiguration.ApplyServiceMigrationSettings(new ServiceOptions
        {
            Name = "configuration",
            SigningKey = configurationKey.ExportECPrivateKeyPem()
        });
        var payments = DisputePortal.TestFixtures.TestRuntimeConfiguration.ApplyServiceMigrationSettings(new ServiceOptions
        {
            Name = "payments",
            SigningKey = paymentsKey.ExportECPrivateKeyPem()
        });
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Development",
            ContentRootPath = Path.GetTempPath()
        });
        builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
        {
            options.Cookie.Name = "country-policy-native-fixture";
            options.Cookie.SecurePolicy = CookieSecurePolicy.None;
        });
        builder.Services.AddServiceSecurity(validator, ServiceSecurity.InternalServiceScheme);
        builder.Services.AddSingleton(CountryProbe());
        builder.Services.AddControllers().AddApplicationPart(typeof(CountryPolicyController).Assembly);
        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<PortalExceptionHandler>();
        builder.Services.AddHsts(_ =>
        {
        });
        await using var app = builder.Build();
        app.UseExceptionHandler();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        app.MapGet(
            "/fixture/admin-cookie",
            async (HttpContext context) =>
        {
            await context.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, OtherCustomer.ToString()), new Claim(ClaimTypes.Role, "super_admin")],
                        CookieAuthenticationDefaults.AuthenticationScheme)));
            return Results.NoContent();
        }).AllowAnonymous();
        await app.StartAsync(ct);
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            BaseAddress = new Uri(address)
        };
        const string route = "/api/internal/country-policy/eligibility";
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync(route, ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await http.GetAsync("/fixture/admin-cookie", ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync(route, ct)).StatusCode);
        async Task<HttpResponseMessage> Probe(string token)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, route);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return await http.SendAsync(request, ct);
        }

        using var wrongService = await Probe(ServiceSecurity.Token(payments, "identity", null));
        Assert.Equal(HttpStatusCode.Forbidden, wrongService.StatusCode);
        using var wrongAudience = await Probe(ServiceSecurity.Token(configuration, "payments", null));
        Assert.Equal(HttpStatusCode.Unauthorized, wrongAudience.StatusCode);
        var forged = new JwtSecurityToken(
            "configuration",
            "identity",
            [new Claim("caller", "configuration")],
            DateTime.UtcNow.AddSeconds(-2),
            DateTime.UtcNow.AddSeconds(60),
            new SigningCredentials(
                new ECDsaSecurityKey(paymentsKey)
                {
                    KeyId = "payments",
                    CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false }
                },
                SecurityAlgorithms.EcdsaSha256));
        using var forgedCaller = await Probe(new JwtSecurityTokenHandler().WriteToken(forged));
        Assert.Equal(HttpStatusCode.Unauthorized, forgedCaller.StatusCode);
        using var valid = await Probe(ServiceSecurity.Token(configuration, "identity", null));
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        var eligibility = await valid.Content.ReadFromJsonAsync<CountryChangeEligibility>(ct);
        Assert.NotNull(eligibility);
        Assert.False(eligibility.HasPendingRefunds);
        Assert.False(eligibility.HasUnfinishedOperations);
        Assert.True(valid.Headers.CacheControl?.NoStore);
        await app.StopAsync(ct);
    }
}
