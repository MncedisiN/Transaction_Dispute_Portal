using Dapper;
using System.Data.Common;
using DisputePortal.Api.Models.Domain;
using DisputePortal.Platform.Infrastructure.Sql;

namespace DisputePortal.Api.Repository; 
internal static class DisputeNotificationOutbox
{
    internal static async Task Enqueue(DbConnection c, DbTransaction tx, Guid id, string milestone, CancellationToken ct)
    {
        var customer = await c.ExecuteScalarAsync<Guid>(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/DisputeNotificationOutbox/GetDisputeNotificationContext"),
            new { Id = id },
            tx,
            cancellationToken: ct));
        var cycle = await c.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT
                "transaction_disputes"."workflowCycle" AS "workflow_cycle"
            FROM "disputes"."disputes" AS "transaction_disputes"
            WHERE "transaction_disputes"."publicId" = @Id
            """,
            new { Id = id },
            tx,
            cancellationToken: ct));
        var recipients = new HashSet<Guid>
        {
            customer
        };
        if (milestone == "Started")
        {
            var queue = await c.ExecuteScalarAsync<string>(new CommandDefinition(
                """
                SELECT
                    "transaction_disputes"."queueType" AS "queue_type"
                FROM "disputes"."disputes" AS "transaction_disputes"
                WHERE "transaction_disputes"."publicId" = @Id
                """,
                new { Id = id },
                tx,
                cancellationToken: ct));
            var staff = await c.QueryAsync<Guid>(new CommandDefinition(
                SqlScripts.ReadSqlScript("Shared/Queries/Api/DisputeNotificationOutbox/GetActiveUsers"),
                new { Specialist = CaseServicePolicy.SpecialistFor(queue ?? "General") },
                tx,
                cancellationToken: ct));
            foreach (var person in staff)
            {
                recipients.Add(person);
                await c.ExecuteAsync(new CommandDefinition(
                    SqlScripts.ReadSqlScript("Shared/Queries/Api/DisputeNotificationOutbox/InsertNotificationByIdAndUserAndDispute"),
                    new
                    {
                        Id = Guid.NewGuid(),
                        User = person,
                        Dispute = id
                    },
                    tx,
                    cancellationToken: ct));
            }
        }

        foreach (var user in recipients)
        {
            var channel = await c.ExecuteScalarAsync<string>(new CommandDefinition(
                SqlScripts.ReadSqlScript("Shared/Queries/Api/DisputeNotificationOutbox/GetCustomerProfileForUser"),
                new { User = user },
                tx,
                cancellationToken: ct)) ?? "inapp";
            foreach (var deliveryChannel in (milestone == "Completed" ? new[]
            {
                channel
            }

            : new[]
            {
                "push"
            }

            ).Where(value => value != "inapp").Distinct())
            {
                await c.ExecuteAsync(new CommandDefinition(
                    SqlScripts.ReadSqlScript("Shared/Queries/Api/DisputeNotificationOutbox/InsertDelivery"),
                    new
                    {
                        Id = Guid.NewGuid(),
                        User = user,
                        Dispute = id,
                        Milestone = milestone,
                        Channel = deliveryChannel,
                        Cycle = cycle,
                        Now = DateTimeOffset.UtcNow
                    },
                    tx,
                    cancellationToken: ct));
            }
        }
    }
     
    internal static async Task EnqueueEvent(
        DbConnection c,
        DbTransaction tx,
        Guid? disputeId,
        Guid eventId,
        string kind,
        string title,
        string body,
        IEnumerable<Guid>? extraRecipients,
        CancellationToken ct,
        string? url = null,
        Guid? supportThread = null)
    {
        if (eventId == Guid.Empty || string.IsNullOrWhiteSpace(kind) || kind.Length > 64)
        {
            throw new ArgumentException("A notification needs a stable action identifier and kind.");
        }

        var recipients = new HashSet<Guid>(extraRecipients ?? []);
        Guid? customer = null;
        if (disputeId.HasValue)
        {
            customer = await c.QuerySingleAsync<Guid>(new CommandDefinition(
                SqlScripts.ReadSqlScript("Shared/Queries/Api/DisputeNotificationOutbox/GetDisputeNotificationContext"),
                new { Id = disputeId },
                tx,
                cancellationToken: ct));
            recipients.Add(customer.Value);
        }

        foreach (var user in recipients.Where(value => value != Guid.Empty))
        {
            var userKey = await c.QuerySingleOrDefaultAsync<long?>(new CommandDefinition(
                """
                SELECT
                    "users"."id" AS "row_id"
                FROM "identity"."users" AS "users"
                WHERE "users"."publicId" = @User
                AND "users"."accountStatus" = 'Active'
                """,
                new { User = user },
                tx,
                cancellationToken: ct));
            if (userKey is null)
            {
                continue;
            }

            if (await c.ExecuteScalarAsync<int>(new CommandDefinition(
                """
                SELECT
                    COUNT(*)
                FROM "notifications"."events" AS "notification_events"
                WHERE "notification_events"."userId" = @UserKey
                AND "notification_events"."eventKey" = @EventId
                """,
                new { UserKey = userKey, EventId = eventId },
                tx,
                cancellationToken: ct)) != 0)
            {
                continue;
            }

            var notificationId = Guid.NewGuid();
            var target = url ?? (disputeId is null ? "/notifications" : user == customer ? $"/disputes/{disputeId}" : $"/admin/disputes/{disputeId}");
            if (!target.StartsWith('/') || target.StartsWith("//") || target.Contains('\\') || target.Length > 500)
            {
                throw new ArgumentException("Notification navigation must stay inside the portal.");
            }

            await c.ExecuteAsync(new CommandDefinition(
                SqlScripts.ReadSqlScript("Shared/Queries/Api/DisputeNotificationOutbox/InsertNotificationByIdAndUserAndDisputeAndKindAndTitle"),
                new
                {
                    Id = notificationId,
                    User = user,
                    Dispute = disputeId,
                    Kind = kind,
                    Title = title,
                    Body = body
                },
                tx,
                cancellationToken: ct));
            await c.ExecuteAsync(new CommandDefinition(
                SqlScripts.ReadSqlScript("Shared/Queries/Api/DisputeNotificationOutbox/InsertEvent"),
                new
                {
                    Id = Guid.NewGuid(),
                    EventId = eventId,
                    UserKey = userKey,
                    Notification = notificationId,
                    Dispute = disputeId,
                    Kind = kind,
                    Url = target,
                    Now = DateTimeOffset.UtcNow
                },
                tx,
                cancellationToken: ct));
            if (supportThread.HasValue)
            {
                await c.ExecuteAsync(new CommandDefinition(
                    SqlScripts.ReadSqlScript("Shared/Queries/Api/DisputeNotificationOutbox/UpdateEventsForEvent"),
                    new
                    {
                        Thread = supportThread,
                        EventId = eventId,
                        UserKey = userKey
                    },
                    tx,
                    cancellationToken: ct));
            }
        }
    }
}
