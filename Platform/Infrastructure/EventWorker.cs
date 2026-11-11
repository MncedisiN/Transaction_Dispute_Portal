using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using DisputePortal.Platform.Contracts;
using DisputePortal.Platform.Infrastructure.Interfaces;

namespace DisputePortal.Platform.Infrastructure;

public class EventWorker(
    ServiceDatabase db,
    ServiceOptions options,
    EventBus bus,
    IEnumerable<IDomainEventConsumer> consumers,
    ILogger<EventWorker> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.WhenAll(
        PublishLoop(stoppingToken),
        ConsumeLoop(stoppingToken));

    private async Task PublishLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var c = await db.OpenServiceDatabaseConnection(ct);
                var lease = Guid.NewGuid();
                var row = await c.QuerySingleOrDefaultAsync<Pending>(new CommandDefinition(db.Dialect.ClaimOutbox, new
                {
                    Lease = lease,
                    LeaseSeconds = options.OutboxLeaseSeconds
                }, cancellationToken: ct));
                if (row is null)
                {
                    await Task.Delay(options.OutboxPollMilliseconds, ct);
                    continue;
                }

                DomainEvent? value = null;
                try
                {
                    value = JsonSerializer.Deserialize<DomainEvent>(row.Body, DomainEvent.Json) ?? throw new InvalidOperationException();
                    await bus.PublishDomainEvent(value, ct);
                    await c.ExecuteAsync(new CommandDefinition(db.Dialect.PublishedOutbox, new
                    {
                        Id = row.EventId,
                        Lease = lease
                    }, cancellationToken: ct));
                    PlatformMetrics.RecordPublished();
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception error)
                {
                    PlatformMetrics.RecordFailure();
                    await c.ExecuteAsync(new CommandDefinition(
                        db.Dialect.RetryOutbox,
                        new
                        {
                            Id = row.EventId,
                            Lease = lease,
                            Error = error.GetType().Name,
                            Delay = (int)Math.Min(options.MaximumRetryDelaySeconds, Math.Pow(2, Math.Min(30, row.Attempts + 1)))
                        },
                        cancellationToken: ct));
                    logger.LogWarning(error,
                        "Event {EventId} retained in outbox after {ErrorType}. CorrelationId {CorrelationId}",
                        row.EventId, error.GetType().Name, value?.CorrelationId.ToString("D"));
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception error)
            {
                logger.LogWarning(error, "Outbox temporarily unavailable {ErrorType}", error.GetType().Name);
                await Task.Delay(options.RecoveryDelayMilliseconds, ct);
            }
        }
    }

    private async Task ConsumeLoop(CancellationToken ct)
    {
        if (!options.BrokerEnabled || options.Subscriptions.Length == 0)
        {
            return;
        }

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var connection = await bus.Factory().CreateConnectionAsync(ct);
                await using var channel = await connection.CreateChannelAsync(cancellationToken: ct);
                await bus.Topology(channel, ct);
                await channel.BasicQosAsync(0, (ushort)options.Prefetch, false, ct);
                var consumer = new AsyncEventingBasicConsumer(channel);
                consumer.ReceivedAsync += async (_, delivery) =>
                {
                    DomainEvent? value = null;
                    string? signature = null;
                    try
                    {
                        object? signatureValue = null;
                        delivery.BasicProperties.Headers?.TryGetValue("signature", out signatureValue);
                        signature = signatureValue is byte[] bytes ? System.Text.Encoding.UTF8.GetString(bytes) : signatureValue?.ToString();
                        value = EventSignature.Verify(delivery.Body.Span, signature, options);
                        using var activity = PlatformMetrics.StartEventActivity(
                            "event.consume", value, System.Diagnostics.ActivityKind.Consumer,
                            EventTraceContext.Read(delivery.BasicProperties.Headers));
                        using var scope = logger.BeginScope(new Dictionary<string, object?>
                        {
                            ["CorrelationId"] = value.CorrelationId.ToString("D"),
                            ["TraceId"] = activity?.TraceId.ToString()
                        });
                        var started = System.Diagnostics.Stopwatch.GetTimestamp();
                        var successful = false;
                        try
                        {
                            foreach (var handler in consumers.Where(c => c.Subscriptions.Any(pattern => EventRouting.Matches(pattern, value.EventType))))
                            {
                                await handler.Handle(value, ct);
                            }

                            await channel.BasicAckAsync(delivery.DeliveryTag, false, ct);
                            PlatformMetrics.RecordConsumed();
                            successful = true;
                        }
                        finally
                        {
                            PlatformMetrics.RecordDuration(started, value.EventType, successful);
                            if (!successful)
                            {
                                activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error);
                            }
                        }
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                    }
                    catch (Exception error)
                    {
                        PlatformMetrics.RecordFailure();
                        var attempt = delivery.BasicProperties.Headers?.TryGetValue("attempt", out var raw) == true ? Convert.ToInt32(raw) : 0;
                        try
                        {
                            if (value is null || attempt >= options.MaximumAttempts)
                            {
                                await channel.BasicNackAsync(delivery.DeliveryTag, false, false, ct);
                                logger.LogError(
                                    error,
                                    "Event {EventId} dead-lettered after {ErrorType}. CorrelationId {CorrelationId}",
                                    value?.EventId,
                                    error.GetType().Name,
                                    value?.CorrelationId.ToString("D"));
                            }
                            else
                            {
                                await bus.PublishDomainEvent(
                                    value,
                                    ct,
                                    EventBus.RetryExchange,
                                    options.Name + ".retry." + (attempt + 1),
                                    attempt + 1,
                                    signature);
                                await channel.BasicAckAsync(delivery.DeliveryTag, false, ct);
                            }
                        }
                        catch (Exception)
                        {
                            if (channel.IsOpen)
                            {
                                await channel.BasicNackAsync(delivery.DeliveryTag, false, true, ct);
                            }
                        }
                    }
                };
                await channel.BasicConsumeAsync("portal." + options.Name, false, consumer, ct);
                bus.MarkConsumerConnection(true);
                while (connection.IsOpen && !ct.IsCancellationRequested)
                    await Task.Delay(options.BrokerConnectionPollMilliseconds, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception error)
            {
                bus.MarkConsumerConnection(false);
                logger.LogWarning(error, "Consumer reconnecting after {ErrorType}", error.GetType().Name);
                await Task.Delay(options.RecoveryDelayMilliseconds, ct);
            }
            finally
            {
                bus.MarkConsumerConnection(false);
            }
        }
    }

    private class Pending
    {
        public Guid EventId { get; set; }
        public string Body { get; set; } = "";
        public int Attempts { get; set; }
    }
}
