using Dapper;
using DisputePortal.Platform.Contracts;
using DisputePortal.Platform.Infrastructure;
using DisputePortal.Platform.Infrastructure.Models;
using DisputePortal.Platform.Notifications.Interfaces;
using DisputePortal.Platform.Notifications.Models;
using DisputePortal.Platform.Infrastructure.Sql;

namespace DisputePortal.Platform.Notifications;

public class NotificationStore(ServiceDatabase db) : INotificationStore
{
    public static readonly string Schema = SqlScripts.ReadSqlScript("PostgreSql/Schema/Notifications/NotificationStore/CreateNotificationSchema");

    public static bool Supports(DomainEvent e) => e.EventType is EventTypes.TransactionCompleted or EventTypes.TransactionFailed
        && e.DeserializePayload<TransactionEvent>().Kind is "Refund" or "Recall";

    public Task Request(DomainEvent e, CancellationToken ct) => db.ProcessEventWithInboxDeduplication(
        e,
        async (c, tx) =>
    {
        if (!Supports(e))
        {
            return;
        }

        var result = e.DeserializePayload<TransactionEvent>();
        var owner = result.UserId;
        var aggregate = result.TransactionId;
        var template = result.Kind == "Recall" ? "dispute.refund.recalled" : e.EventType == EventTypes.TransactionCompleted ? "dispute.refund.completed" : "dispute.refund.failed";
        var n = new NotificationEvent
        {
            NotificationId = Guid.NewGuid(),
            UserId = owner,
            TemplateKey = template,
            AggregateId = aggregate
        };
        await c.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO notifications."notifications"(
                    "publicId",
                    "customerPublicId",
                    "sourceEventPublicId",
                    "aggregatePublicId",
                    "templateKey"
                )
                VALUES (
                    @NotificationId,
                    @UserId,
                    @Source,
                    @AggregateId,
                    @TemplateKey
                )
                """,
                new
                {
                    n.NotificationId,
                    n.UserId,
                    Source = e.EventId,
                    n.AggregateId,
                    n.TemplateKey
                },
                tx,
                cancellationToken: ct));
        await ServiceDatabase.SaveDomainEventToOutbox(
                c,
                tx,
                DomainEvent.CreateDomainEvent(
                    "notifications",
                    EventTypes.NotificationRequested,
                    n,
                    e.CorrelationId,
                    e.EventId,
                    e.TransactionReference),
                ct);
    },
        ct);

    public async Task Deliver(CancellationToken ct)
    {
        await using var c = await db.OpenServiceDatabaseConnection(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        var pending = await c.QueryAsync<PendingNotification>(new CommandDefinition(
            SqlScripts.ReadSqlScript("PostgreSql/Queries/Notifications/NotificationStore/GetPendingNotifications"),
            transaction: tx,
            cancellationToken: ct));
        foreach (var n in pending)
        {
            await c.ExecuteAsync(new CommandDefinition(
                """
                UPDATE notifications."notifications"
                SET
                    status = 'Delivered',
                    "deliveredAt" = now()
                WHERE "publicId" = @NotificationId
                """,
                n,
                tx,
                cancellationToken: ct));
            var requested = System.Text.Json.JsonSerializer.Deserialize<DomainEvent>(n.RequestBody, DomainEvent.Json)!;
            var sent = new NotificationEvent
            {
                NotificationId = n.NotificationId,
                UserId = n.UserId,
                TemplateKey = n.TemplateKey,
                AggregateId = n.AggregateId,
                Channel = n.Channel
            };
            await ServiceDatabase.SaveDomainEventToOutbox(
                c,
                tx,
                DomainEvent.CreateDomainEvent(
                    "notifications",
                    EventTypes.NotificationSent,
                    sent,
                    requested.CorrelationId,
                    requested.EventId,
                    requested.TransactionReference),
                ct);
        }

        await tx.CommitAsync(ct);
    }

    public async Task<object> GetCustomerNotifications(Guid user, CancellationToken ct)
    {
        await using var c = await db.OpenServiceDatabaseConnection(ct);
        return await c.QueryAsync<NotificationItem>(new CommandDefinition(
            SqlScripts.ReadSqlScript("PostgreSql/Queries/Notifications/NotificationStore/GetLatestDeliveredNotificationsForUser"),
            new
            {
                User = user
            },
            cancellationToken: ct));
    }

    public async Task MarkNotificationRead(Guid id, Guid user, CancellationToken ct)
    {
        await using var c = await db.OpenServiceDatabaseConnection(ct);
        if (await c.ExecuteAsync(new CommandDefinition(
            """
            UPDATE notifications."notifications"
            SET
                "readAt" = COALESCE("readAt", now())
            WHERE "publicId" = @Id
            AND "customerPublicId" = @User
            AND "templateKey" LIKE 'dispute.refund.%'
            """,
            new
            {
                Id = id,
                User = user
            },
            cancellationToken: ct)) != 1)
        {
            throw new PlatformException(404, "notification_missing", "Notification not found.");
        }
    }
}
