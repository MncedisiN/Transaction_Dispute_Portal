using System.Text;
using Dapper;
using DisputePortal.Platform.Infrastructure.Models;
using DisputePortal.Platform.Infrastructure.Sql;

namespace DisputePortal.Platform.Infrastructure;

public class EventRecovery(ServiceDatabase db, ServiceOptions options, EventBus bus)
{
    public async Task<object> Status(CancellationToken ct)
    {
        await using var c = await db.OpenServiceDatabaseConnection(ct);
        var pending = await c.ExecuteScalarAsync<long>(new CommandDefinition(
            """
            SELECT
                COUNT(*)
            FROM service_outbox
            WHERE state <> 'Published'
            """,
            cancellationToken: ct));
        var oldest = await c.ExecuteScalarAsync<DateTime?>(new CommandDefinition(
            """
            SELECT
                MIN(created_at)
            FROM service_outbox
            WHERE state <> 'Published'
            """,
            cancellationToken: ct));
        return new
        {
            service = options.Name,
            transport = options.BrokerEnabled ? "rabbitmq" : "http",
            brokerEnabled = options.BrokerEnabled,
            brokerConnected = options.BrokerEnabled && bus.IsConnected,
            pending,
            oldestPendingSeconds = oldest is null ? 0 : Math.Max(
            0,
            (DateTime.UtcNow - DateTime.SpecifyKind(oldest.Value, DateTimeKind.Utc)).TotalSeconds),
            processed = await c.ExecuteScalarAsync<long>(new CommandDefinition(
            """
            SELECT
                COUNT(*)
            FROM service_inbox
            """,
            cancellationToken: ct))
        };
    }

    public async Task<object> Replay(ReplayDeadLetters request, Guid actor, CancellationToken ct)
    {
        if (!options.BrokerEnabled)
        {
            throw new PlatformException(
                409,
                "broker_disabled",
                "RabbitMQ dead-letter replay requires the broker transport. Pending HTTP deliveries retry from the outbox.");
        }

        if (request.Maximum is < 1 or > 100 || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 500)
        {
            throw new PlatformException(400, "replay_invalid", "Choose 1–100 events and give a replay reason.");
        }

        await using var connection = await bus.Factory().CreateConnectionAsync(ct);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: ct);
        var count = 0;
        for (var i = 0; i < request.Maximum; i++)
        {
            var message = await channel.BasicGetAsync("portal." + options.Name + ".dead", false, ct);
            if (message is null)
            {
                break;
            }

            try
            {
                if (message.BasicProperties.Headers?.TryGetValue("signature", out var raw) != true)
                {
                    throw new InvalidOperationException("Missing producer signature.");
                }

                var signature = raw is byte[] bytes ? Encoding.UTF8.GetString(bytes) : raw?.ToString();
                var value = EventSignature.Verify(message.Body.Span, signature, options);
                await Audit(actor, "DeadLetterReplayRequested", value.EventId, request.Reason, ct);
                await bus.PublishDomainEvent(value, ct, EventBus.RetryExchange, options.Name, 0, signature);
                await channel.BasicAckAsync(message.DeliveryTag, false, ct);
                count++;
                await Audit(actor, "DeadLetterReplayed", value.EventId, "Confirmed delivery to the service queue.", ct);
            }
            catch
            {
                if (channel.IsOpen)
                {
                    await channel.BasicNackAsync(message.DeliveryTag, false, true, CancellationToken.None);
                }

                throw new PlatformException(
                    409,
                    "replay_deferred",
                    "The event remains in the dead-letter queue. Inspect its signature, schema and consumer failure before replaying it.");
            }
        }

        return new
        {
            replayed = count
        };
    }

    private async Task Audit(Guid actor, string action, Guid id, string detail, CancellationToken ct)
    {
        await using var c = await db.OpenServiceDatabaseConnection(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await ServiceDatabase.InsertServiceAuditEvent(c, tx, actor, action, id, detail, ct);
        await tx.CommitAsync(ct);
    }
}
