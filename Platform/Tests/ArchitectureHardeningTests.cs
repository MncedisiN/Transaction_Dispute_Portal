using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using DisputePortal.Platform.Contracts;
using DisputePortal.Platform.Infrastructure;
using DisputePortal.Platform.Infrastructure.Interfaces;
using DisputePortal.Platform.Infrastructure.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DisputePortal.Platform.Tests;

public class ArchitectureHardeningTests
{
    [Theory]
    [InlineData("accounts")]
    [InlineData("audit")]
    [InlineData("configuration")]
    [InlineData("ledger")]
    [InlineData("notifications")]
    [InlineData("payments")]
    [InlineData("reporting")]
    [InlineData("scheduling")]
    [InlineData("transactions")]
    [InlineData("debt")]
    public void Release_routing_metadata_matches_every_financial_modules_actual_consumers(string name)
    {
        using var configuration = new ConfigurationManager();
        configuration.AddIntegrationDefaults();
        var options = configuration.GetRequiredSection("Platform").Get<ServiceOptions>()!;
        options.Name = name;
        options.Endpoints = options.EventRoutes.Keys.ToDictionary(key => key, key => "https://" + key + ".example.test");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(options);
        services.AddSingleton<ServiceDatabase>();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IServiceClient>(new Targets());
        switch (name)
        {
            case "accounts": Accounts.AccountsModule.ConfigureServices(services); break;
            case "audit": Audit.AuditModule.ConfigureServices(services); break;
            case "configuration": Configuration.ConfigurationModule.ConfigureServices(services); break;
            case "ledger": Ledger.LedgerModule.ConfigureServices(services); break;
            case "notifications": Notifications.NotificationsModule.ConfigureServices(services); break;
            case "payments": Payments.PaymentsModule.ConfigureServices(services); break;
            case "reporting": Reporting.ReportingModule.ConfigureServices(services); break;
            case "scheduling": Scheduling.SchedulingModule.ConfigureServices(services); break;
            case "transactions": Transactions.TransactionsModule.ConfigureServices(services); break;
            case "debt": ReversalReviews.ReversalReviewsModule.ConfigureServices(services); break;
        }

        using var provider = services.BuildServiceProvider();
        options.Subscriptions = provider.GetServices<IDomainEventConsumer>().SelectMany(consumer => consumer.Subscriptions).Distinct().ToArray();
        EventRouting.ValidateSubscriptions(options);
    }

    [Fact]
    public async Task Native_resilience_never_retries_commands_and_isolates_each_authority()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var configuration = new ConfigurationManager();
        configuration.AddIntegrationDefaults();
        configuration["ServiceHttpResilience:Retry:Delay"] = "00:00:00.001";
        var options = configuration.GetRequiredSection("Platform").Get<ServiceOptions>()!;
        options.Name = "identity";
        options.SigningKey = key.ExportECPrivateKeyPem();
        options.Endpoints = new() { ["failed"] = "https://failed.example.test", ["healthy"] = "https://healthy.example.test" };
        var handler = new Reply();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(options);
        services.AddServiceHttpClient(configuration).ConfigurePrimaryHttpMessageHandler(() => handler);
        await using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IServiceClient>();
        await Assert.ThrowsAsync<PlatformException>(() => client.SendServiceRequest<object>("failed", HttpMethod.Post, "/command"));
        Assert.Equal(1, handler.Failed);
        for (var i = 0; i < 4; i++)
        {
            await Assert.ThrowsAsync<PlatformException>(() => client.SendServiceRequest<object>("failed", HttpMethod.Post, "/command"));
        }

        await Assert.ThrowsAsync<PlatformException>(() => client.SendServiceRequest<object>("failed", HttpMethod.Post, "/command"));
        Assert.Equal(5, handler.Failed); // The sixth request is rejected by the native circuit.
        var correlation = Guid.NewGuid();
        using var activity = new Activity("test.request").Start();
        activity.SetTag("portal.correlation_id", correlation.ToString("D"));
        await client.SendServiceRequest<object>("healthy", HttpMethod.Get, "/resource");
        Assert.Equal(1, handler.Healthy);
        Assert.Equal(correlation.ToString("D"), handler.Correlation);
    }

    [Fact]
    public async Task Http_event_delivery_contacts_only_subscribed_receivers_and_retains_misconfigured_events()
    {
        using var configuration = new ConfigurationManager();
        configuration.AddIntegrationDefaults();
        var options = configuration.GetRequiredSection("Platform").Get<ServiceOptions>()!;
        options.Name = "identity";
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        options.SigningKey = key.ExportECPrivateKeyPem();
        options.Endpoints = new() { ["audit"] = "https://audit.example.test", ["payments"] = "https://payments.example.test" };
        options.EventRoutes = new() { ["audit"] = ["#"], ["payments"] = [EventTypes.TransactionCompleted] };
        var client = new Targets();
        await using var bus = new EventBus(options, client);
        var value = DomainEvent.CreateDomainEvent("identity", EventTypes.CaseStatusesChanged, new { revision = 1 });
        await bus.PublishDomainEvent(value, default);
        Assert.Equal(new[] { "audit" }, client.Called);
        options.EventRoutes.Clear();
        await Assert.ThrowsAsync<InvalidOperationException>(() => bus.PublishDomainEvent(value, default));
        Assert.Single(client.Called);
    }

    [Fact]
    public void Routing_configuration_cannot_silently_disagree_with_registered_consumers()
    {
        using var configuration = new ConfigurationManager();
        configuration.AddIntegrationDefaults();
        var options = configuration.GetRequiredSection("Platform").Get<ServiceOptions>()!;
        options.Name = "audit";
        options.Endpoints = options.EventRoutes.Keys.ToDictionary(name => name, name => "https://" + name + ".example.test");
        options.Subscriptions = ["#"];
        EventRouting.ValidateSubscriptions(options);
        options.Subscriptions = ["transaction.completed"];
        Assert.Throws<InvalidOperationException>(() => EventRouting.ValidateSubscriptions(options));
        options.Subscriptions = ["#"];
        options.Endpoints.Remove("ledger");
        Assert.Throws<InvalidOperationException>(() => EventRouting.ValidateSubscriptions(options));
    }

    [Fact]
    public void Native_event_metrics_are_observable_bounded_and_free_of_payload_dimensions()
    {
        var observed = new ConcurrentQueue<(string Name, double Value, string[] Tags)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meter) =>
        {
            if (instrument.Meter.Name == PlatformMetrics.InstrumentationName)
            {
                meter.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((i, v, tags, _) => observed.Enqueue((i.Name, v, tags.ToArray().Select(t => t.Key).ToArray())));
        listener.SetMeasurementEventCallback<double>((i, v, tags, _) => observed.Enqueue((i.Name, v, tags.ToArray().Select(t => t.Key).ToArray())));
        listener.Start();
        PlatformMetrics.RecordPublished();
        PlatformMetrics.RecordConsumed();
        PlatformMetrics.RecordFailure();
        PlatformMetrics.RecordDuration(Stopwatch.GetTimestamp(), "untrusted-dynamic-type", true);
        listener.RecordObservableInstruments();
        Assert.Contains(observed, item => item.Name == "portal.events.published" && item.Value >= 1);
        Assert.Contains(observed, item => item.Name == "portal.events.consumed" && item.Value >= 1);
        Assert.Contains(observed, item => item.Name == "portal.events.failed" && item.Value >= 1);
        var duration = Assert.Single(observed, item => item.Name == "portal.event.processing.duration");
        Assert.Equal(new[] { "messaging.message.type", "outcome" }, duration.Tags);
        Assert.DoesNotContain(observed.SelectMany(item => item.Tags), tag => tag.Contains("id", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Event_traces_propagate_w3c_context_without_changing_the_signed_v1_envelope()
    {
        var stopped = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == PlatformMetrics.InstrumentationName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity => stopped.Add(activity)
        };
        ActivitySource.AddActivityListener(listener);
        var value = DomainEvent.CreateDomainEvent("identity", EventTypes.CaseStatusesChanged, new { revision = 1 });
        var headers = new Dictionary<string, object?>();
        ActivityContext parent;
        using (var activity = PlatformMetrics.StartEventActivity("event.publish", value, ActivityKind.Producer))
        {
            Assert.NotNull(activity);
            EventTraceContext.Write(headers);
            parent = activity.Context;
        }

        using (var consumer = PlatformMetrics.StartEventActivity("event.consume", value, ActivityKind.Consumer, EventTraceContext.Read(headers)))
        {
            Assert.NotNull(consumer);
            Assert.Equal(parent.TraceId, consumer.TraceId);
            Assert.Equal(parent.SpanId, consumer.ParentSpanId);
            Assert.DoesNotContain(consumer.TagObjects, item => item.Key.Contains("account", StringComparison.OrdinalIgnoreCase));
        }

        Assert.Equal(2, stopped.Count);
        Assert.Equal(default, EventTraceContext.Read(new Dictionary<string, object?> { ["traceparent"] = new string('x', 10000) }));
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(value, DomainEvent.Json));
        Assert.Equal(new[] { "eventId", "eventType", "eventVersion", "timestamp", "correlationId", "causationId", "transactionReference", "sourceService", "payload" }, json.RootElement.EnumerateObject().Select(p => p.Name));
        var restored = JsonSerializer.Deserialize<DomainEvent>(json.RootElement, DomainEvent.Json)!;
        restored.ValidateEventEnvelope();
        Assert.Equal(value.EventId, restored.EventId);
    }

    [Fact]
    public void Native_trace_export_retains_route_and_status_without_financial_paths_or_error_messages()
    {
        using var activity = new Activity("GET /accounts/customer-secret?token=secret").Start();
        activity.SetTag("http.request.method", "GET");
        activity.SetTag("http.route", "/accounts/{id:guid}");
        activity.SetTag("http.response.status_code", 500);
        activity.SetTag("url.full", "https://service/accounts/customer-secret?token=secret");
        activity.SetTag("exception.message", "private SQL and account values");
        activity.SetTag("account.id", "customer-secret");
        activity.SetStatus(ActivityStatusCode.Error, "private failure details");
        new TracePrivacyProcessor().OnEnd(activity);
        Assert.Equal("GET /accounts/{id:guid}", activity.DisplayName);
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Null(activity.StatusDescription);
        Assert.Null(activity.GetTagItem("url.full"));
        Assert.Null(activity.GetTagItem("exception.message"));
        Assert.Null(activity.GetTagItem("account.id"));
        Assert.Equal(500, activity.GetTagItem("http.response.status_code"));
    }

    private sealed class Reply : HttpMessageHandler
    {
        public int Failed;
        public int Healthy;
        public string? Correlation;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.Host == "failed.example.test")
            {
                Interlocked.Increment(ref Failed);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("{}") });
            }

            Interlocked.Increment(ref Healthy);
            Correlation = request.Headers.TryGetValues("X-Correlation-ID", out var values) ? values.Single() : null;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        }
    }

    private sealed class Targets : IServiceClient
    {
        public List<string> Called { get; } = [];
        public Task<T> SendServiceRequest<T>(string service, HttpMethod method, string path, object? body = null, System.Security.Claims.ClaimsPrincipal? actor = null, CancellationToken ct = default)
        {
            Called.Add(service);
            return Task.FromResult(default(T)!);
        }
    }
}
