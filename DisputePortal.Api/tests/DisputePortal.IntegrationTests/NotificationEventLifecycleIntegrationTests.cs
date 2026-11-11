using Dapper;
using DisputePortal.Api.Repository;
using DisputePortal.Api.Repository.Data;
using Xunit;

namespace DisputePortal.IntegrationTests;

public sealed partial class DisputeWorkflowRepositoryTests
{
    [Fact]
    public async Task Transfer_details_are_persisted_and_balance_projection_pushes_are_suppressed()
    {
        var ct = CancellationToken.None;
        await using var c = await _connections.OpenConnection(ct);
        var body = "Transferred 125.00 ZAR from Cheque ••••1234 to Savings ••••5678.";
        var eventId = Guid.NewGuid();
        var noise = Guid.NewGuid();
        await c.ExecuteAsync(
            """
            INSERT INTO "notifications"."notifications"("publicId",
                "userId",
                "notificationType",
                "title",
                "body")
            VALUES(@Id,
                (
                    SELECT "resolved1"."id"
                    FROM "identity"."users" AS "resolved1"
                    WHERE "resolved1"."publicId" = @User),
                'TransferCompleted',
                'Dispute Portal',
                @Body);
            INSERT INTO "notifications"."events"("publicId",
                "eventKey",
                "userId",
                "notificationId",
                "kind",
                "url",
                "state",
                "attempts",
                "nextAttemptAt",
                "createdAt")
            SELECT @Event,
            @Event,
            "u"."id",
            "n"."id",
            'TransferCompleted',
            '/transactions/example',
            'Pending',
            0,
            @Now,
            @Now
            FROM "notifications"."notifications" n
            JOIN "identity"."users" u
            ON "u"."id" = "n"."userId"
            WHERE "n"."publicId" = @Id
            """,
            new
            {
                Id = Guid.NewGuid(),
                User = Customer,
                Body = body,
                Event = eventId,
                Now = DateTimeOffset.UtcNow
            });
        await c.ExecuteAsync(
            """
            INSERT INTO "notifications"."notifications"("publicId",
                "userId",
                "notificationType",
                "title",
                "body")
            VALUES(@Id,
                (
                    SELECT "resolved1"."id"
                    FROM "identity"."users" AS "resolved1"
                    WHERE "resolved1"."publicId" = @User),
                'AccountBalanceUpdated',
                'Balance',
                'Old balance projection');
            INSERT INTO "notifications"."events"("publicId",
                "eventKey",
                "userId",
                "notificationId",
                "kind",
                "url",
                "state",
                "attempts",
                "nextAttemptAt",
                "createdAt")
            SELECT @Event,
            @Event,
            "u"."id",
            "n"."id",
            'AccountBalanceUpdated',
            '/notifications',
            'Pending',
            0,
            @Now,
            @Now
            FROM "notifications"."notifications" n
            JOIN "identity"."users" u
            ON "u"."id" = "n"."userId"
            WHERE "n"."publicId" = @Id
            """,
            new
            {
                Id = Guid.NewGuid(),
                User = Customer,
                Event = noise,
                Now = DateTimeOffset.UtcNow
            });
        var repository = new NotificationRepository(
            _connections,
            DatabaseDialect.GetDatabaseDialect(_provider),
            new TestProtector(),
            _repository,
            _repository, DisputePortal.TestFixtures.TestRuntimeConfiguration.Read());
        var claims = await repository.ClaimNotifications(ct);
        Assert.Contains(claims, row => row.Id == eventId);
        Assert.DoesNotContain(claims, row => row.Id == noise);
        Assert.Equal(body, (await repository.GetPushContext(Customer, eventId, ct))!.Body);
        Assert.Null(await repository.GetPushContext(OtherCustomer, eventId, ct));
        Assert.Equal(
            "Skipped",
            await c.ExecuteScalarAsync<string>(
                """
            SELECT "notification_events"."state" AS "state"
            FROM "notifications"."events" AS "notification_events"
            WHERE "notification_events"."publicId" = @Id
            """,
                new { Id = noise }));
    }

    [Fact]
    public async Task Assignment_and_withdrawal_push_events_are_claimed_scoped_completed_and_count_reads_once()
    {
        var ct = CancellationToken.None;
        var reviewed = await ReviewableRefundCase();
        await _repository.WithdrawCustomerDispute(Customer, reviewed.Id, reviewed.Version, "event-lifecycle", ct);
        var notifications = new NotificationRepository(
            _connections,
            DatabaseDialect.GetDatabaseDialect(_provider),
            new TestProtector(),
            _repository,
            _repository, DisputePortal.TestFixtures.TestRuntimeConfiguration.Read());
        var claimed = await notifications.ClaimNotifications(ct);
        var assigned = Assert.Single(
            claimed,
            item => item.UserId == Customer && item.DisputeId == reviewed.Id && item.Milestone == "Event:CaseAssigned");
        var withdrawn = Assert.Single(
            claimed,
            item => item.UserId == Customer && item.DisputeId == reviewed.Id && item.Milestone == "Event:DisputeWithdrawn");
        Assert.Equal("push", assigned.Channel);
        Assert.Equal("push", withdrawn.Channel);
        var unread = await notifications.GetUnreadNotificationCount(Customer, ct);
        var assignmentContext = await notifications.GetPushContext(Customer, assigned.Id, ct);
        Assert.NotNull(assignmentContext);
        Assert.Equal("CaseAssigned", assignmentContext.Kind);
        Assert.Equal($"/disputes/{reviewed.Id}", assignmentContext.Url);
        var context = await notifications.GetPushContext(Customer, withdrawn.Id, ct);
        Assert.NotNull(context);
        Assert.Equal("DisputeWithdrawn", context.Kind);
        Assert.Equal($"/disputes/{reviewed.Id}", context.Url);
        Assert.Equal(unread, context.UnreadCount);
        Assert.Null(await notifications.GetPushContext(OtherCustomer, withdrawn.Id, ct));
        Assert.Null(await notifications.GetPushContext(Agent, withdrawn.Id, ct));
        await using var c = await _connections.OpenConnection(ct);
        var notification = await c.ExecuteScalarAsync<Guid>(
            """
            SELECT "n"."publicId" AS "id"
            FROM "notifications"."events" e
            JOIN "notifications"."notifications" n
            ON "n"."id" = "e"."notificationId"
            WHERE "e"."publicId" = @Id
            """,
            new { Id = withdrawn.Id });
        Assert.False(await notifications.MarkNotificationRead(OtherCustomer, notification, ct));
        Assert.Equal(unread, await notifications.GetUnreadNotificationCount(Customer, ct));
        Assert.True(await notifications.MarkNotificationRead(Customer, notification, ct));
        Assert.Equal(unread - 1, await notifications.GetUnreadNotificationCount(Customer, ct));
        Assert.Equal(unread - 1, (await notifications.GetPushContext(Customer, withdrawn.Id, ct))!.UnreadCount);
        await notifications.MarkNotificationRead(Customer, notification, ct);
        Assert.Equal(unread - 1, await notifications.GetUnreadNotificationCount(Customer, ct));
        await notifications.CompleteNotification(assigned.Id, "Sent", null, ct);
        await notifications.CompleteNotification(withdrawn.Id, "Sent", null, ct);
        Assert.Equal(
            2,
            await c.ExecuteScalarAsync<int>(
                """
            SELECT COUNT(*)
            FROM "notifications"."events" AS "notification_events"
            WHERE("notification_events"."publicId" = @Assigned
                OR "notification_events"."publicId" = @Withdrawn)
            AND "notification_events"."state" = 'Sent'
            AND "notification_events"."attempts" = 1
            AND "notification_events"."completedAt" IS NOT NULL
            AND "notification_events"."leaseUntil" IS NULL
            """,
                new { Assigned = assigned.Id, Withdrawn = withdrawn.Id }));
        var next = await notifications.ClaimNotifications(ct);
        Assert.DoesNotContain(next, item => item.Id == assigned.Id || item.Id == withdrawn.Id);
    }
}
