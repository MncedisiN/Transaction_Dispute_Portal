using System.Text.Json;
using RabbitMQ.Client;
using DisputePortal.Platform.Contracts;
using DisputePortal.Platform.Infrastructure.Models;
using DisputePortal.Platform.Infrastructure.Interfaces;

namespace DisputePortal.Platform.Infrastructure;

public class EventBus(ServiceOptions options, IServiceClient? client = null) : IAsyncDisposable
{
    public const string Exchange = "portal.events", RetryExchange = "portal.retry", DeadExchange = "portal.dead";
    private IConnection? connection;
    private IChannel? publisher;
    private readonly SemaphoreSlim gate = new(1, 1);
    private int consumerConnected;
    public bool IsConnected => connection?.IsOpen == true || Volatile.Read(ref consumerConnected) == 1;

    public void MarkConsumerConnection(bool connected) => Volatile.Write(ref consumerConnected, connected ? 1 : 0);

    public ConnectionFactory Factory() => new()
    {
        HostName = options.BrokerHost,
        Port = options.BrokerPort,
        VirtualHost = options.BrokerVirtualHost,
        UserName = options.BrokerUser,
        Password = options.BrokerPassword,
        AutomaticRecoveryEnabled = true,
        TopologyRecoveryEnabled = true,
        NetworkRecoveryInterval = TimeSpan.FromSeconds(options.BrokerRecoverySeconds),
        RequestedHeartbeat = TimeSpan.FromSeconds(options.BrokerHeartbeatSeconds),
        ClientProvidedName = "dispute-" + options.Name,
        Ssl = new SslOption
        {
            Enabled = options.BrokerTls,
            Version = TransportSecurity.Protocols,
            AcceptablePolicyErrors = System.Net.Security.SslPolicyErrors.None,
            ServerName = options.BrokerServerName ?? options.BrokerHost
        }
    };

    public async Task Topology(IChannel channel, CancellationToken ct)
    {
        await channel.ExchangeDeclareAsync(Exchange, ExchangeType.Topic, true, false, cancellationToken: ct);
        await channel.ExchangeDeclareAsync(RetryExchange, ExchangeType.Direct, true, false, cancellationToken: ct);
        await channel.ExchangeDeclareAsync(DeadExchange, ExchangeType.Direct, true, false, cancellationToken: ct);
        if (options.Subscriptions.Length == 0)
        {
            return;
        }

        var queue = "portal." + options.Name;
        await channel.QueueDeclareAsync(
            queue + ".dead",
            true,
            false,
            false,
            new Dictionary<string, object?> { { "x-queue-type", "quorum" } },
            cancellationToken: ct);
        await channel.QueueBindAsync(queue + ".dead", DeadExchange, options.Name, cancellationToken: ct);
        await channel.QueueDeclareAsync(
            queue,
            true,
            false,
            false,
            new Dictionary<string, object?> { { "x-queue-type", "quorum" }, { "x-dead-letter-exchange", DeadExchange }, { "x-dead-letter-routing-key", options.Name } },
            cancellationToken: ct);
        foreach (var key in options.Subscriptions)
            await channel.QueueBindAsync(queue, Exchange, key, cancellationToken: ct);
        await channel.QueueBindAsync(queue, RetryExchange, options.Name, cancellationToken: ct);
        for (var attempt = 1; attempt <= options.MaximumAttempts; attempt++)
        {
            var retry = queue + ".retry." + attempt;
            await channel.QueueDeclareAsync(
                retry,
                true,
                false,
                false,
                new Dictionary<string, object?> { { "x-queue-type", "quorum" }, { "x-message-ttl", (int)Math.Min(
                options.MaximumRetryDelaySeconds * 1000,
                options.RetryMilliseconds * Math.Pow(2, attempt - 1)) }, { "x-dead-letter-exchange", RetryExchange }, { "x-dead-letter-routing-key", options.Name } },
                cancellationToken: ct);
            await channel.QueueBindAsync(retry, RetryExchange, options.Name + ".retry." + attempt, cancellationToken: ct);
        }
    }

    public async Task PublishDomainEvent(
        DomainEvent value,
        CancellationToken ct,
        string? exchange = null,
        string? route = null,
        int attempt = 0,
        string? originalSignature = null)
    {
        value.ValidateEventEnvelope();
        using var activity = PlatformMetrics.StartEventActivity("event.publish", value, System.Diagnostics.ActivityKind.Producer);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(options.BrokerEnabled ? options.BrokerPublishTimeoutSeconds : options.HttpEventTimeoutSeconds));
        try
        {
            await PublishSignedEvent(value, deadline.Token, exchange, route, attempt, originalSignature);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error);
            throw;
        }
    }

    private async Task PublishSignedEvent(
        DomainEvent value,
        CancellationToken ct,
        string? exchange,
        string? route,
        int attempt,
        string? originalSignature)
    {
        if (exchange is null && value.SourceService != options.Name)
        {
            throw new InvalidOperationException("A service may publish only its own domain events.");
        }

        if (!options.BrokerEnabled)
        {
            if (exchange is not null || route is not null || originalSignature is not null)
            {
                throw new InvalidOperationException("Broker replay requires RabbitMQ to be enabled.");
            }

            if (client is null || options.Endpoints.Count == 0)
            {
                throw new InvalidOperationException("HTTP event delivery requires configured service endpoints.");
            }

            var bytes = JsonSerializer.SerializeToUtf8Bytes(value, DomainEvent.Json);
            if (bytes.Length > options.MaximumEventBytes)
            {
                throw new InvalidOperationException("Event exceeds the configured size limit.");
            }

            using var signer = System.Security.Cryptography.ECDsa.Create();
            signer.ImportFromPem(options.SigningKey);
            var delivery = new SignedEventDelivery
            {
                Body = bytes,
                Signature = Convert.ToBase64String(signer.SignData(bytes, System.Security.Cryptography.HashAlgorithmName.SHA256))
            };

            // Deliver financial payloads only to declared consumers. Partial retries are deduplicated by each inbox.
            var failures = new System.Collections.Concurrent.ConcurrentQueue<Exception>();
            await Parallel.ForEachAsync(
                EventRouting.Receivers(options, value.EventType),
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = options.HttpEventConcurrency,
                    CancellationToken = ct
                },
                async (target, token) =>
            {
                try
                {
                    await client.SendServiceRequest<object>(target, HttpMethod.Post, HttpEventEndpoint.Path, delivery, ct: token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception error)
                { 
                    failures.Enqueue(error);
                }
            });
            if (!failures.IsEmpty)
            {
                throw new AggregateException("Some event receivers require retry.", failures);
            }

            return;
        }

        await gate.WaitAsync(ct);
        try
        {
            if (connection?.IsOpen != true || publisher?.IsOpen != true)
            {
                if (publisher is not null)
                {
                    await publisher.DisposeAsync();
                }

                if (connection is not null)
                {
                    await connection.DisposeAsync();
                }

                connection = await Factory().CreateConnectionAsync(ct);
                publisher = await connection.CreateChannelAsync(new CreateChannelOptions(true, true), ct);
                await Topology(publisher, ct);
            }

            var body = JsonSerializer.SerializeToUtf8Bytes(value, DomainEvent.Json);
            if (body.Length > options.MaximumEventBytes)
            {
                throw new InvalidOperationException("Event exceeds the configured size limit.");
            }

            string signature;
            if (originalSignature is not null)
            {
                signature = originalSignature;
            }
            else
            {
                if (value.SourceService != options.Name)
                {
                    throw new InvalidOperationException("A retry requires the producer signature.");
                }

                using var signer = System.Security.Cryptography.ECDsa.Create();
                signer.ImportFromPem(options.SigningKey);
                signature = Convert.ToBase64String(signer.SignData(body, System.Security.Cryptography.HashAlgorithmName.SHA256));
            }

            var properties = new BasicProperties
            {
                Persistent = true,
                ContentType = "application/json",
                MessageId = value.EventId.ToString(),
                Type = value.EventType,
                CorrelationId = value.CorrelationId.ToString(),
                Timestamp = new AmqpTimestamp(value.Timestamp.ToUnixTimeSeconds()),
                Headers = new Dictionary<string, object?>
                {
                    {
                        "attempt",
                        attempt
                    },
                    {
                        "source",
                        value.SourceService
                    },
                    {
                        "signature",
                        signature
                    }
                }
            };
            EventTraceContext.Write(properties.Headers);
            await publisher!.BasicPublishAsync(exchange ?? Exchange, route ?? value.EventType, true, properties, body, ct);
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (publisher is not null)
        {
            await publisher.DisposeAsync();
        }

        if (connection is not null)
        {
            await connection.DisposeAsync();
        }

        gate.Dispose();
    }
}
