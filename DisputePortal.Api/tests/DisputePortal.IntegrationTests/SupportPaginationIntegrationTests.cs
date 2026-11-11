using Dapper;
using DisputePortal.Api.Models.Internal;
using DisputePortal.Api.Repository;
using DisputePortal.Api.Repository.Data;
using Xunit;

namespace DisputePortal.IntegrationTests;

public sealed partial class DisputeWorkflowRepositoryTests
{
    [Fact]
    public async Task Conversation_paging_reaches_history_beyond_one_hundred_without_crossing_ownership()
    {
        var ct = CancellationToken.None;
        var ids = Enumerable.Range(0, 105).Select(_ => Guid.NewGuid()).ToArray();
        for (var index = 0; index < ids.Length; index++)
            await SeedPagingConversation(ids[index], Customer, index, true, false, null, ct);
        var foreign = Guid.NewGuid();
        await SeedPagingConversation(foreign, OtherCustomer, 0, true, false, null, ct);
        var active = Guid.NewGuid();
        await SeedPagingConversation(active, Customer, 500, false, false, null, ct);
        var repository = new SupportRepository(_connections, DatabaseDialect.GetDatabaseDialect(_provider), TimeZoneInfo.Utc);
        var all = new List<Guid>();
        for (var page = 1; page <= 6; page++)
        {
            var result = await repository.GetThreads(Customer, ct, page: page, pageSize: 20, ownOnly: true);
            Assert.Equal(106, result.Total);
            Assert.Equal(page, result.Page);
            Assert.Equal(20, result.PageSize);
            Assert.All(result.Items, row => Assert.Equal(Customer, row.CustomerId));
            Assert.All(result.Items.Where(row => row.Status == "Closed"), row => Assert.False(row.CanReply));
            if (page == 1)
            {
                Assert.Equal(ids[0], result.Items[0].Id);
            }

            all.AddRange(result.Items.Select(row => row.Id));
        }

        Assert.Equal(106, all.Count);
        Assert.Equal(106, all.Distinct().Count());
        Assert.Contains(ids[^1], all);
        Assert.Equal(active, all[^1]);
        Assert.DoesNotContain(foreign, all);
        var beyondEnd = await repository.GetThreads(Customer, ct, page: 999, pageSize: 20, ownOnly: true);
        Assert.Empty(beyondEnd.Items);
        Assert.Equal(106, beyondEnd.Total);
        var clamped = await repository.GetThreads(Customer, ct, page: -1, pageSize: 500, ownOnly: true);
        Assert.Equal(1, clamped.Page);
        Assert.Equal(100, clamped.PageSize);
    }

    [Fact]
    public async Task Unread_assignment_and_workspace_filters_are_applied_before_the_page_limit()
    {
        var ct = CancellationToken.None;
        await using (var connection = await _connections.OpenConnection(ct))
            await connection.ExecuteAsync(new CommandDefinition(
            """
                INSERT INTO "identity"."userRoles"("userId", "roleId")
                SELECT actor.id, role.id
                FROM "identity".users actor
                CROSS JOIN "identity".roles role
                WHERE actor."publicId" = @Actor
                    AND role.code = 'super_admin'
                """,
            new { Actor = OtherCustomer },
            cancellationToken: ct));
        for (var index = 0; index < 101; index++)
            await SeedPagingConversation(Guid.NewGuid(), Customer, index, true, false, null, ct);
        var wanted = Guid.NewGuid();
        await SeedPagingConversation(wanted, Customer, 200, true, true, OtherCustomer, ct);
        var own = Guid.NewGuid();
        await SeedPagingConversation(own, OtherCustomer, 0, true, false, null, ct);
        var repository = new SupportRepository(_connections, DatabaseDialect.GetDatabaseDialect(_provider), TimeZoneInfo.Utc);
        var unread = await repository.GetThreads(
            Customer,
            ct,
            filter: new HistoryFilter { UnreadOnly = true },
            page: 1,
            pageSize: 20,
            ownOnly: true);
        Assert.Equal(1, unread.Total);
        Assert.Equal(wanted, Assert.Single(unread.Items).Id);
        var assigned = await repository.GetThreads(OtherCustomer, ct, page: 1, pageSize: 20, assignedOnly: true);
        Assert.Equal(1, assigned.Total);
        Assert.Equal(wanted, Assert.Single(assigned.Items).Id);
        var customerWorkspace = await repository.GetThreads(OtherCustomer, ct, page: 1, pageSize: 20, ownOnly: true);
        Assert.Equal(1, customerWorkspace.Total);
        Assert.Equal(own, Assert.Single(customerWorkspace.Items).Id);
    }

    private async Task SeedPagingConversation(
        Guid id,
        Guid owner,
        int ageMinutes,
        bool closed,
        bool unread,
        Guid? assigned,
        CancellationToken ct)
    {
        await using var connection = await _connections.OpenConnection(ct);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO support.conversations(
                "publicId", "customerId", "concernId", subject, "createdAt", "assignedToId")
            VALUES (
                @Id,
                (SELECT id FROM "identity".users WHERE "publicId" = @Owner),
                (SELECT id FROM support.concerns WHERE code = 'General'),
                @Subject, @At,
                (SELECT id FROM "identity".users WHERE "publicId" = @Assigned));
            INSERT INTO support.messages("publicId", "threadId", "authorId", body, "createdAt", "clientMessageId", "requestSubject")
            VALUES (
                @Message,
                (SELECT id FROM support.conversations WHERE "publicId" = @Id),
                (SELECT id FROM "identity".users WHERE "publicId" = @Author),
                'Retained conversation history for native pagination verification.', @At, @Request, @Subject);
            """,
            new
            {
                Id = id,
                Owner = owner,
                Subject = "Paging " + id,
                At = DateTimeOffset.UtcNow.AddMinutes(-ageMinutes),
                Assigned = assigned,
                Message = Guid.NewGuid(),
                Author = unread ? Agent : owner,
                Request = Guid.NewGuid()
            },
            cancellationToken: ct));
        if (closed)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE support.conversations
                SET "conversationStatus" = 'Closed',
                    "closedAt" = @Now,
                    "closedById" = "customerId",
                    "closureReason" = 'Completed enquiry retained for native pagination verification.'
                WHERE "publicId" = @Id
                """,
                new { Id = id, Now = DateTimeOffset.UtcNow },
                cancellationToken: ct));
        }
    }
}
