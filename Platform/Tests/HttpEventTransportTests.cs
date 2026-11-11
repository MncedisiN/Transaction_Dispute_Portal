using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Dapper;
using DisputePortal.Platform.Accounts;
using DisputePortal.Platform.Audit;
using DisputePortal.Platform.Configuration;
using DisputePortal.Platform.Contracts;
using DisputePortal.Platform.ReversalReviews;
using DisputePortal.Platform.Infrastructure;
using DisputePortal.Platform.Ledger;
using DisputePortal.Platform.Notifications;
using DisputePortal.Platform.Payments;
using DisputePortal.Platform.Reporting;
using DisputePortal.Platform.Scheduling;
using DisputePortal.Platform.Transactions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using DisputePortal.Platform.Infrastructure.Models;
using DisputePortal.Platform.Infrastructure.Interfaces;

namespace DisputePortal.Platform.Tests;

public partial class ServiceDomainMigrationTests
{
    [NativePostgreSqlFact]
    public async Task Http_transport_completes_transfer_refund_and_recall_without_a_broker_and_retries_safely()
    {
        await using var cluster = new HttpCluster();
        await cluster.Start();
        var owner = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var admin = Actor(adminId, "super_admin");
        var customer = Actor(owner, "customer");
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var debit = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        AccountHistorySnapshot Book(Guid id, string number, string type, decimal balance, PriorTransaction[] entries)
        {
            var book = new AccountHistorySnapshot
            {
                SnapshotId = Guid.NewGuid(),
                ActorId = adminId,
                SourceSystem = "dispute-portal-sql",
                CapturedAt = now,
                AccountId = id,
                UserId = owner,
                Number = number,
                AccountType = type,
                CountryCode = "ZA",
                Currency = "ZAR",
                CreatedAt = now.AddDays(-5),
                ExpectedBalance = balance,
                ExpectedReserved = 0,
                Transactions = entries
            };
            return new global::DisputePortal.Platform.Contracts.AccountHistorySnapshot(book)
            {
                Hash = book.ComputeHash()
            };
        }

        var book = Book(
            first,
            "1234567890",
            "Cheque",
            900,
            [new global::DisputePortal.Platform.Contracts.PriorTransaction {
                Id = Guid.NewGuid(),
                Reference = "OPEN-CREDIT",
                OccurredAt = now.AddDays(-3),
                Description = "Opening credit",
                MerchantName = "Bank",
                Amount = 1000,
                Currency = "ZAR",
                Type = "Credit",
                Direction = "Credit",
                Status = "Posted",
                DisputeEligible = false,
                CurrentDelta = 1000,
                ReservedDelta = 0,
                Unknown = 0
            }, new global::DisputePortal.Platform.Contracts.PriorTransaction {
                Id = debit,
                Reference = "ORIGINAL-DEBIT",
                OccurredAt = now.AddHours(-1),
                Description = "Card purchase",
                MerchantName = "Merchant",
                Amount = 100,
                Currency = "ZAR",
                Type = "CardPurchase",
                Direction = "Debit",
                Status = "Posted",
                DisputeEligible = true,
                CurrentDelta = -100,
                ReservedDelta = 0,
                Unknown = 0
            }]);
        var other = Book(second, "1234567891", "Savings", 0, []);
        foreach (var prior in new[]
        {
            book,
            other
        }

        )
            foreach (var name in new[]
            {
                "accounts",
                "ledger",
                "transactions"
            }

            )
                await cluster.Client.SendServiceRequest<JsonElement>(name, HttpMethod.Post, "/migration/book", prior, admin);
        async Task<AccountRecord[]> Accounts() => await cluster.Client.SendServiceRequest<AccountRecord[]>(
            "accounts",
            HttpMethod.Get,
            "/accounts",
            actor: customer);
        await Eventually(
            Accounts,
            rows => rows.Length == 2 && rows.All(x => x.Status == "Active") && rows.Single(x => x.Id == first).LedgerBalance == 900);
        var quote = await cluster.Client.SendServiceRequest<PaymentQuote>(
            "payments",
            HttpMethod.Post,
            "/quotes",
            new PaymentQuoteRequest("Transfer", first, second, 25),
            customer);
        var payment = await cluster.Client.SendServiceRequest<PaymentRecord>(
            "payments",
            HttpMethod.Post,
            "/payments",
            new PaymentRequest(Guid.NewGuid(), "Transfer", first, second, 25, QuoteId: quote.Id),
            customer);
        await Eventually(Accounts, rows => rows.Single(x => x.Id == second).LedgerBalance == 25);
        var afterTransfer = await Accounts();
        Assert.Equal(900 - 25 - quote.Fee, afterTransfer.Single(x => x.Id == first).LedgerBalance);
        var refundId = Guid.NewGuid();
        var financial = new DisputeFinancialRequest
        {
            TransactionId = refundId,
            RequestId = Guid.NewGuid(),
            DisputeId = Guid.NewGuid(),
            OriginalTransactionId = debit,
            UserId = owner,
            AccountId = first,
            Kind = "Refund",
            Amount = 100,
            Currency = "ZAR",
            Reference = "RF-" + refundId.ToString("N"),
            ActorId = adminId
        };
        var refund = DomainEvent.CreateDomainEvent("identity", "dispute.financial.requested", financial);
        await cluster.Client.SendServiceRequest<CountryOperation>(
            "configuration",
            HttpMethod.Post,
            "/country-operations",
            new CountryOperationRequest
            {
                RequestId = financial.RequestId,
                OperationId = refundId,
                SourceKind = "DisputeFinance",
                CountryCode = "ZA",
                Currency = "ZAR",
                OriginalTransactionId = debit
            },
            admin);
        await cluster.Enqueue(refund);
        await Eventually(Accounts, rows => rows.Single(x => x.Id == first).LedgerBalance == 975 - quote.Fee);
        await using var ledger = await cluster.Databases["ledger"].OpenServiceDatabaseConnection();
        Assert.Equal(
            1,
            await ledger.ExecuteScalarAsync<int>("SELECT count(*) FROM ledger.journals WHERE \"transactionPublicId\"=@Id", new
            {
                Id = refundId
            }));
        // Identical event delivered again over real authenticated HTTP: no second credit.
        await cluster.Bus.PublishDomainEvent(refund, default);
        await cluster.Bus.PublishDomainEvent(refund, default);
        Assert.Equal(
            1,
            await ledger.ExecuteScalarAsync<int>("SELECT count(*) FROM ledger.journals WHERE \"transactionPublicId\"=@Id", new
            {
                Id = refundId
            }));
        var recallId = Guid.NewGuid();
        var recall = new global::DisputePortal.Platform.Contracts.DisputeFinancialRequest(financial)
        {
            TransactionId = recallId,
            RequestId = Guid.NewGuid(),
            OriginalTransactionId = refundId,
            Kind = "Recall",
            Reference = "RC-" + recallId.ToString("N")
        };
        await cluster.Client.SendServiceRequest<CountryOperation>(
            "configuration",
            HttpMethod.Post,
            "/country-operations",
            new CountryOperationRequest
            {
                RequestId = recall.RequestId,
                OperationId = recallId,
                SourceKind = "DisputeFinance",
                CountryCode = "ZA",
                Currency = "ZAR",
                OriginalTransactionId = refundId
            },
            admin);
        await cluster.Enqueue(DomainEvent.CreateDomainEvent("identity", "dispute.financial.requested", recall));
        await Eventually(Accounts, rows => rows.Single(x => x.Id == first).LedgerBalance == 875 - quote.Fee);
        // Original payment remains an unchanged debit, independently of refund/recall journals.
        var original = await cluster.Client.SendServiceRequest<TransactionRecord>(
            "transactions",
            HttpMethod.Get,
            "/transactions/" + debit,
            actor: customer);
        Assert.Equal(100, original.Amount);
        Assert.Equal("ZAR", original.Currency);
        var eventValue = DomainEvent.CreateDomainEvent(
            "identity",
            EventTypes.UserRegistered,
            new UserRegisteredEvent
            {
                UserId = Guid.NewGuid(),
                CountryCode = "ZA",
                AccountCurrency = "ZAR"
            });
        var options = cluster.Options["identity"];
        var destination = options.Endpoints["audit"];
        options.Endpoints["audit"] = "http://127.0.0.1:1";
        await cluster.Enqueue(eventValue);
        await using var identity = await cluster.Databases["identity"].OpenServiceDatabaseConnection();
        await Eventually(
            () => identity.ExecuteScalarAsync<int>(
            "SELECT attempts FROM service_outbox WHERE event_id=@Id",
            new { Id = eventValue.EventId }),
            attempts => attempts > 0);
        Assert.NotEqual(
            "Published",
            await identity.ExecuteScalarAsync<string>("SELECT state FROM service_outbox WHERE event_id=@Id", new
            {
                Id = eventValue.EventId
            }));
        options.Endpoints["audit"] = destination;

        await Eventually(
            () => identity.ExecuteScalarAsync<string>(
            "SELECT state FROM service_outbox WHERE event_id=@Id",
            new { Id = eventValue.EventId }),
            state => state == "Published");
        await using var audit = await cluster.Databases["audit"].OpenServiceDatabaseConnection();
        Assert.Equal(
            1,
            await audit.ExecuteScalarAsync<int>("SELECT count(*) FROM service_inbox WHERE event_id=@Id", new
            {
                Id = eventValue.EventId
            }));
        Assert.All(cluster.Options.Values, o => Assert.False(o.BrokerEnabled));
    }

    [NativePostgreSqlFact]
    public async Task Http_receiver_requires_service_authentication_valid_signature_source_and_bounded_body()
    {
        await using var cluster = new HttpCluster();
        await cluster.Start(identityOnly: true);
        var options = cluster.Options["identity"];
        using var http = new HttpClient
        {
            BaseAddress = new Uri(options.Endpoints["identity"])
        };
        var value = DomainEvent.CreateDomainEvent("identity", EventTypes.CaseStatusesChanged, new
        {
            revision = 1
        });
        using var key = ECDsa.Create();
        key.ImportFromPem(options.SigningKey);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, DomainEvent.Json);
        var delivery = new SignedEventDelivery
        {
            Body = bytes,
            Signature = Convert.ToBase64String(key.SignData(bytes, HashAlgorithmName.SHA256))
        };
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.PostAsJsonAsync(HttpEventEndpoint.Path, delivery)).StatusCode);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ServiceSecurity.Token(options, "identity", null));
        Assert.Equal(HttpStatusCode.Conflict, (await http.PostAsJsonAsync(HttpEventEndpoint.Path, delivery)).StatusCode); // Never acknowledge an event with no consumer.
        delivery.Signature = Convert.ToBase64String(new byte[64]);
        Assert.Equal(HttpStatusCode.BadRequest, (await http.PostAsJsonAsync(HttpEventEndpoint.Path, delivery)).StatusCode);
        Assert.Equal(
            HttpStatusCode.RequestEntityTooLarge,
            (await http.PostAsync(HttpEventEndpoint.Path, new StringContent(new string('x', options.MaximumEventBytes * 2)))).StatusCode);
        var foreign = DomainEvent.CreateDomainEvent(
            "accounts",
            EventTypes.AccountActivated,
            new AccountEvent
            {
                AccountId = Guid.NewGuid(),
                UserId = Guid.NewGuid(),
                CountryCode = "ZA",
                Currency = "ZAR",
                AccountType = "Cheque"
            });
        using var foreignKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        options.PublicKeys["accounts"] = foreignKey.ExportSubjectPublicKeyInfoPem();
        bytes = JsonSerializer.SerializeToUtf8Bytes(foreign, DomainEvent.Json);
        delivery = new SignedEventDelivery
        {
            Body = bytes,
            Signature = Convert.ToBase64String(foreignKey.SignData(bytes, HashAlgorithmName.SHA256))
        };
        Assert.Equal(HttpStatusCode.Forbidden, (await http.PostAsJsonAsync(HttpEventEndpoint.Path, delivery)).StatusCode);
    }

    private static ClaimsPrincipal Actor(Guid id, string role) => new(new ClaimsIdentity(
        [new Claim(ClaimTypes.NameIdentifier, id.ToString()), new Claim(ClaimTypes.Role, role)],
        "test"));

    private static async Task<T> Eventually<T>(Func<Task<T>> read, Func<T, bool> ready)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        while (true)
        {
            var value = await read();
            if (ready(value))
            {
                return value;
            }

            await Task.Delay(100, timeout.Token);
        }
    }

    private class HttpCluster : IAsyncDisposable
    {
        private readonly List<WebApplication> apps = [];
        private readonly List<DatabaseFixture> fixtures = [];
        private readonly Dictionary<string, string> endpoints = [];
        private readonly Dictionary<string, string> publicKeys = [];
        private readonly Dictionary<string, string[]> routes = [];
        public Dictionary<string, ServiceOptions> Options { get; } = [];
        public Dictionary<string, ServiceDatabase> Databases { get; } = [];
        public IServiceClient Client => apps[0].Services.GetRequiredService<IServiceClient>();
        public EventBus Bus => apps[0].Services.GetRequiredService<EventBus>();

        public async Task Start(bool identityOnly = false)
        {
            PlatformMapping.InitializeDapperMappings();
            string[] names = identityOnly ? ["identity"] : ["identity", "configuration", "accounts", "ledger", "transactions", "payments", "audit", "notifications", "reporting", "scheduling", "debt"];
            foreach (var name in names)
            {
                using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
                var configuration = new ConfigurationManager();
                configuration.AddIntegrationDefaults();
                var options = configuration.GetSection("Platform").Get<ServiceOptions>()!;
                options.Name = name;
                options.BrokerEnabled = false;
                options.BrokerHost = "unused.invalid";
                options.BrokerPort = 0;
                options.SigningKey = key.ExportECPrivateKeyPem();
                options.PublicKeys = publicKeys;
                publicKeys[name] = key.ExportSubjectPublicKeyInfoPem();
                options.Endpoints = endpoints;
                options.EventRoutes = routes;
                options.OutboxPollMilliseconds = 50;
                var fixture = await DatabaseFixture.Create(name);
                fixtures.Add(fixture);
                Options[name] = options;
                Databases[name] = fixture.Database;
            }

            foreach (var name in names)
                switch (name)
                {
                    case "identity":
                        await StartHost<EmptyIdentity>(name);
                        break;
                    case "configuration":
                        await StartHost<ConfigurationModule>(name);
                        break;
                    case "accounts":
                        await StartHost<AccountsModule>(name);
                        break;
                    case "ledger":
                        await StartHost<LedgerModule>(name);
                        break;
                    case "transactions":
                        await StartHost<TransactionsModule>(name);
                        break;
                    case "payments":
                        await StartHost<PaymentsModule>(name);
                        break;
                    case "audit":
                        await StartHost<AuditModule>(name);
                        break;
                    case "notifications":
                        await StartHost<NotificationsModule>(name);
                        break;
                    case "reporting":
                        await StartHost<ReportingModule>(name);
                        break;
                    case "scheduling":
                        await StartHost<SchedulingModule>(name);
                        break;
                    case "debt":
                        await StartHost<ReversalReviewsModule>(name);
                        break;
                }

            foreach (var options in Options.Values)
            {
                options.ValidateServiceConfiguration();
                if (!identityOnly)
                {
                    EventRouting.ValidateSubscriptions(options);
                }
            }
        }

        private async Task StartHost<T>(string name)
            where T : class, IServiceModule
        {
            var options = Options[name];
            var db = Databases[name];
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
            builder.Configuration.AddIntegrationDefaults();
            builder.Configuration["ServiceHttpResilience:AttemptTimeout:Timeout"] = "00:00:30";
            builder.Configuration["ServiceHttpResilience:TotalRequestTimeout:Timeout"] = "00:00:45";
            builder.Configuration["ServiceHttpResilience:CircuitBreaker:SamplingDuration"] = "00:01:00";
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Configuration["Platform:AllowBookMigration"] = "true";
            builder.Logging.ClearProviders();
            builder.Logging.AddSimpleConsole();
            builder.Logging.SetMinimumLevel(LogLevel.Warning);
            builder.Services.AddSingleton(options);
            builder.Services.AddSingleton(db);
            builder.Services.AddServiceSecurity(options);
            builder.Services.AddServiceHttpClient(builder.Configuration);
            builder.Services.AddSingleton<EventBus>();
            builder.Services.AddHostedService<EventWorker>();
            builder.Services.AddHostedService<ModuleWorker>();
            builder.Services.AddProblemDetails();
            builder.Services.AddExceptionHandler<ServiceExceptionHandler>();
            T.ConfigureServices(builder.Services);
            builder.Services.AddSingleton<T>();
            var app = builder.Build();
            apps.Add(app);
            var module = app.Services.GetRequiredService<T>();
            await db.ApplyServiceSchemaMigrations(
                module.Schema,
                name == "identity" ? [] : ServiceMigrations.GetServiceMigrations(name, "PostgreSql"));
            foreach (var initializer in app.Services.GetServices<IServiceInitializer>())
                await initializer.InitializeServiceData(default);
            options.Subscriptions = app.Services.GetServices<IDomainEventConsumer>().SelectMany(x => x.Subscriptions).Distinct().ToArray();
            routes[name] = options.Subscriptions;
            app.UseExceptionHandler();
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapHttpEventDelivery();
            module.MapServiceEndpoints(app);
            await app.StartAsync();
            endpoints[name] = app.Urls.Single();
        }

        public async Task Enqueue(DomainEvent value)
        {
            await using var c = await Databases["identity"].OpenServiceDatabaseConnection();
            await using var tx = await c.BeginTransactionAsync();
            await ServiceDatabase.SaveDomainEventToOutbox(c, tx, value, default);
            await tx.CommitAsync();
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var app in apps.AsEnumerable().Reverse())
                await app.StopAsync();
            foreach (var app in apps.AsEnumerable().Reverse())
                await app.DisposeAsync();
            foreach (var fixture in fixtures.AsEnumerable().Reverse())
                await fixture.DisposeAsync();
        }
    }

    private class EmptyIdentity : IServiceModule
    {
        public string Schema => "";

        public static void ConfigureServices(IServiceCollection services)
        {
        }

        public void MapServiceEndpoints(WebApplication app)
        {
        }
    }
}
