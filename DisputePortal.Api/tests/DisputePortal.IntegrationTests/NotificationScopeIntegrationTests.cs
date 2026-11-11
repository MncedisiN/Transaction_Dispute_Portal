using Dapper;
using DisputePortal.Api.Repository;
using DisputePortal.Api.Repository.Data;
using Xunit;

namespace DisputePortal.IntegrationTests;

public sealed partial class DisputeWorkflowRepositoryTests
{
    [Fact]
    public async Task Notification_feed_counts_reads_and_pages_only_case_or_support_updates_without_deleting_history()
    {
        var ct = CancellationToken.None;
        var reviewed = await ReviewableRefundCase();
        await using var c = await _connections.OpenConnection(ct);
        var account = Guid.NewGuid();
        var payment = Guid.NewGuid();
        var support = Guid.NewGuid();
        await c.ExecuteAsync(
            """
            INSERT INTO "notifications"."notifications"("publicId",
                "userId",
                "notificationType",
                "title",
                "body")
            VALUES(@Account,
                (
                    SELECT "resolved1"."id"
                    FROM "identity"."users" AS "resolved1"
                    WHERE "resolved1"."publicId" = @Owner),
                'AccountUpdate',
                'Account update',
                'Historical update'),
            (@Payment,
                (
                    SELECT "resolved2"."id"
                    FROM "identity"."users" AS "resolved2"
                    WHERE "resolved2"."publicId" = @Owner),
                'PaymentUpdate',
                'Payment update',
                'Historical update'),
            (@Support,
                (
                    SELECT "resolved3"."id"
                    FROM "identity"."users" AS "resolved3"
                    WHERE "resolved3"."publicId" = @Owner),
                'SupportReply',
                'Support reply',
                'A secure reply is available')
            """,
            new
            {
                Account = account,
                Payment = payment,
                Support = support,
                Owner = Customer
            });
        var store = new NotificationRepository(
            _connections,
            DatabaseDialect.GetDatabaseDialect(_provider),
            new TestProtector(),
            _repository,
            _repository, DisputePortal.TestFixtures.TestRuntimeConfiguration.Read());
        var first = await store.GetNotifications(Customer, 1, 2, ct);
        var second = await store.GetNotifications(Customer, 2, 2, ct);
        var all = await store.GetNotifications(Customer, 1, 100, ct);
        Assert.DoesNotContain(all.Items, item => item.Type is "AccountUpdate" or "PaymentUpdate");
        Assert.Contains(all.Items, item => item.DisputeId == reviewed.Id);
        Assert.Contains(all.Items, item => item.Id == support);
        Assert.All(all.Items, item => Assert.NotEqual(default, item.CreatedAt));
        Assert.Equal(all.Total, first.Total);
        Assert.Equal(all.Total, second.Total);
        Assert.Empty(first.Items.Select(item => item.Id).Intersect(second.Items.Select(item => item.Id)));
        var unread = all.Items.LongCount(item => !item.IsRead);
        Assert.Equal(unread, await store.GetUnreadNotificationCount(Customer, ct));
        Assert.False(await store.MarkNotificationRead(OtherCustomer, support, ct));
        Assert.False(await store.MarkNotificationRead(Customer, account, ct));
        Assert.True(await store.MarkNotificationRead(Customer, support, ct));
        Assert.Equal(unread - 1, await store.GetUnreadNotificationCount(Customer, ct));
        Assert.True(await store.MarkNotificationRead(Customer, support, ct));
        Assert.Equal(unread - 1, await store.GetUnreadNotificationCount(Customer, ct));
        Assert.Equal(
            2,
            await c.ExecuteScalarAsync<int>(
                """
            SELECT COUNT(*)
            FROM "notifications"."notifications" AS "notifications"
            WHERE "notifications"."publicId" IN(@Account,
                @Payment)
            """,
                new { Account = account, Payment = payment }));
        Assert.DoesNotContain((await store.GetNotifications(OtherCustomer, 1, 100, ct)).Items, item => item.Id == support);
    }

    [Fact]
    public async Task Explicit_outbox_claim_projection_preserves_lease_and_payload_for_each_database_provider()
    {
        var ct = CancellationToken.None;
        await using var c = await _connections.OpenConnection(ct);
        var dialect = DisputePortal.Platform.Infrastructure.RelationalDialect.GetDatabaseDialect(_provider.ToString());
        var id = Guid.NewGuid();
        var lease = Guid.NewGuid();
        var correlation = Guid.NewGuid();
        await c.ExecuteAsync(new CommandDefinition(
            dialect.InsertOutbox,
            new
            {
                Id = id,
                Type = "dispute.audit",
                Body = "{}",
                Correlation = correlation
            },
            cancellationToken: ct));
        var row = await c.QuerySingleAsync<ClaimedOutbox>(new CommandDefinition(
            dialect.ClaimOutbox,
            new { LeaseSeconds = 60, Lease = lease },
            cancellationToken: ct));
        Assert.Equal(id, row.EventId);
        Assert.Equal("{}", row.Body);
        Assert.Equal(0, row.Attempts);
        Assert.Equal(
            0,
            await c.ExecuteAsync(new CommandDefinition(
            dialect.PublishedOutbox,
            new { Id = id, Lease = Guid.NewGuid() },
            cancellationToken: ct)));
        Assert.Equal(
            1,
            await c.ExecuteAsync(new CommandDefinition(dialect.PublishedOutbox, new { Id = id, Lease = lease }, cancellationToken: ct)));
        Assert.Null(await c.QuerySingleOrDefaultAsync<ClaimedOutbox>(new CommandDefinition(
            dialect.ClaimOutbox,
            new { LeaseSeconds = 60, Lease = Guid.NewGuid() },
            cancellationToken: ct)));
    }

    private class ClaimedOutbox
    {
        public Guid EventId { get; set; }
        public string Body { get; set; } = null!;
        public int Attempts { get; set; }
    }
}
