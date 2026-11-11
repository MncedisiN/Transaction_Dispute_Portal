using System.Security.Claims;
using System.Security.Cryptography;
using DisputePortal.Platform.Contracts;
using DisputePortal.Platform.Infrastructure;
using Microsoft.Extensions.Configuration;
using Xunit;
using DisputePortal.Platform.Infrastructure.Interfaces;
using DisputePortal.Platform.Infrastructure.Models;

namespace DisputePortal.Platform.Tests;

public class OptionalTransportTests
{
    [Fact]
    public async Task An_unavailable_receiver_does_not_cancel_a_healthy_receivers_transaction()
    {
        using var configuration = new ConfigurationManager();
        configuration.AddIntegrationDefaults();
        var options = configuration.GetSection("Platform").Get<ServiceOptions>()!;
        options.Name = "identity";
        options.BrokerEnabled = false;
        options.Endpoints = new()
        {
            ["failed"] = "http://unused.invalid",
            ["healthy"] = "http://unused.invalid"
        };
        options.EventRoutes = new() { ["failed"] = ["#"], ["healthy"] = ["#"] };
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        options.SigningKey = key.ExportECPrivateKeyPem();
        var client = new ReceiverClient();
        await using var bus = new EventBus(options, client);
        var value = DomainEvent.CreateDomainEvent("identity", EventTypes.CaseStatusesChanged, new
        {
            revision = 1
        });
        await Assert.ThrowsAsync<AggregateException>(() => bus.PublishDomainEvent(value, default));
        Assert.True(client.HealthyCommitted);
        Assert.False(bus.IsConnected);
    }

    [Fact]
    public void Disabled_broker_requires_no_broker_credentials_but_retains_production_database_and_http_tls()
    {
        using var configuration = new ConfigurationManager();
        configuration.AddIntegrationDefaults();
        var options = configuration.GetSection("Platform").Get<ServiceOptions>()!;
        options.BrokerEnabled = false;
        options.BrokerPort = 0;
        options.Endpoints["identity"] = "https://identity.example.test";
        options.PublicKeys["identity"] = "fixture";
        options.ConnectionString = "Host=db.example.test;Database=ledger;SSL Mode=VerifyFull";
        options.ValidateServiceConfiguration();
        TransportSecurity.ValidateServices(options);
        options.Endpoints["identity"] = "http://identity.example.test";
        Assert.Throws<InvalidOperationException>(() => TransportSecurity.ValidateServices(options));
    }

    private class ReceiverClient : IServiceClient
    {
        public bool HealthyCommitted { get; private set; }

        public async Task<T> SendServiceRequest<T>(
            string service,
            HttpMethod method,
            string path,
            object? body = null,
            ClaimsPrincipal? actor = null,
            CancellationToken ct = default)
        {
            if (service == "failed")
            {
                throw new PlatformException(503, "unavailable", "Receiver unavailable.");
            }

            await Task.Delay(50, ct);
            HealthyCommitted = true;
            return default!;
        }
    }
}
