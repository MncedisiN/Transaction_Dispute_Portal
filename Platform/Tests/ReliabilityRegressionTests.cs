using System.Net;
using Microsoft.Extensions.Configuration;
using System.Security.Cryptography;
using System.Text;
using DisputePortal.Platform.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using DisputePortal.Platform.Infrastructure.Interfaces;
using DisputePortal.Platform.Infrastructure.Models;

namespace DisputePortal.Platform.Tests;

public class ReliabilityRegressionTests
{
    [Fact]
    public async Task A_failed_maintenance_job_does_not_starve_later_jobs()
    {
        var executed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failing = new DelegateJob(_ => throw new InvalidOperationException("Isolated failure"));
        var succeeding = new DelegateJob(_ =>
        {
            executed.TrySetResult();
            return Task.CompletedTask;
        });
        using var worker = new ModuleWorker([failing, succeeding], NullLogger<ModuleWorker>.Instance, DeliveryOptions());
        try
        {
            await worker.StartAsync(CancellationToken.None);
            await executed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Shutdown_cancellation_stops_before_the_next_maintenance_job()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var laterRan = false;
        var cancellable = new DelegateJob(async ct =>
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        });
        var later = new DelegateJob(_ =>
        {
            laterRan = true;
            return Task.CompletedTask;
        });
        using var worker = new ModuleWorker([cancellable, later], NullLogger<ModuleWorker>.Instance, DeliveryOptions());
        await worker.StartAsync(CancellationToken.None);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);
        Assert.False(laterRan);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{broken-json")]
    public async Task Missing_or_invalid_required_service_responses_are_sanitized_failures(string body)
    {
        using var http = new HttpClient(new Reply(HttpStatusCode.OK, body));
        var client = new ServiceClient(http, Options());
        var error = await Assert.ThrowsAsync<PlatformException>(() => client.SendServiceRequest<RequiredResult>(
            "target",
            HttpMethod.Get,
            "/resource"));
        Assert.Equal(503, error.Status);
        Assert.Equal("invalid_service_response", error.Code);
        Assert.DoesNotContain(body, error.Message);
    }

    [Fact]
    public async Task A_valid_service_response_preserves_its_contract()
    {
        using var http = new HttpClient(new Reply(HttpStatusCode.OK, "{\"status\":\"Completed\"}"));
        var result = await new ServiceClient(http, Options()).SendServiceRequest<RequiredResult>("target", HttpMethod.Get, "/resource");
        Assert.Equal("Completed", result.Status);
    }

    [Fact]
    public async Task No_content_remains_valid_for_command_endpoints()
    {
        using var http = new HttpClient(new Reply(HttpStatusCode.NoContent, ""));
        var result = await new ServiceClient(http, Options()).SendServiceRequest<object>("target", HttpMethod.Post, "/resource");
        Assert.Null(result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    public void Invalid_prefetch_is_rejected_before_it_can_wrap_or_disable_flow_control(int value)
    {
        var options = DeliveryOptions();
        options.Prefetch = value;
        Assert.Throws<InvalidOperationException>(options.ValidateServiceConfiguration);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(16)]
    [InlineData(65535)]
    public void Supported_prefetch_values_are_preserved(int value)
    {
        var options = DeliveryOptions();
        options.Prefetch = value;
        options.ValidateServiceConfiguration();
        Assert.Equal(value, options.Prefetch);
    }

    [Theory]
    [InlineData(0, 1, 5000)]
    [InlineData(65536, 1, 5000)]
    [InlineData(5673, 0, 5000)]
    [InlineData(5673, 17, 5000)]
    [InlineData(5673, 8, 0)]
    [InlineData(5673, 8, 300001)]
    public void Invalid_queue_topology_settings_fail_fast(int port, int attempts, int retryMilliseconds)
    {
        var options = DeliveryOptions();
        options.BrokerPort = port;
        options.MaximumAttempts = attempts;
        options.RetryMilliseconds = retryMilliseconds;
        Assert.Throws<InvalidOperationException>(options.ValidateServiceConfiguration);
    }

    private static ServiceOptions DeliveryOptions()
    {
        var configuration = new ConfigurationManager();
        configuration.AddIntegrationDefaults();
        var options = configuration.GetSection("Platform").Get<ServiceOptions>()!;
        options.BrokerEnabled = true;
        options.Endpoints["target"] = "http://localhost";
        options.PublicKeys["target"] = "fixture";
        return options;
    }

    private static ServiceOptions Options()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return TestMigrationConfiguration.ApplyServiceMigrationSettings(new ServiceOptions
        {
            Name = "reliability-tests",
            SigningKey = key.ExportECPrivateKeyPem(),
            Endpoints = new()
            {
                ["target"] = "https://example.invalid"
            }
        });
    }

    public class RequiredResult
    {
        public string Status { get; set; } = "";
    }

    private sealed class DelegateJob(Func<CancellationToken, Task> tick) : IPersistentJobRunner
    {
        public Task RunScheduledWork(CancellationToken ct) => tick(ct);
    }

    private sealed class Reply(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        });
    }
}
