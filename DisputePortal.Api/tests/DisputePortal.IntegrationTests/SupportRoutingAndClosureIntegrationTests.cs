using DisputePortal.Api.Models.Domain;
using System.Data.Common;
using Dapper;
using DisputePortal.Api.Models.Exceptions;
using DisputePortal.Api.Models.Internal;
using DisputePortal.Api.Models.Requests;
using DisputePortal.Api.Repository;
using DisputePortal.Api.Repository.Data;
using DisputePortal.Api.Service.Disputes;
using Xunit;

namespace DisputePortal.IntegrationTests;

public sealed partial class DisputeWorkflowRepositoryTests
{
    private async Task<Guid> AddSupportStaff(string role, int capacity = 3)
    {
        var id = Guid.NewGuid();
        await using var c = await _connections.OpenConnection(CancellationToken.None);
        await c.ExecuteAsync(
            """
            INSERT INTO "identity"."users"("publicId",
                "loginIdentifier",
                "normalizedLoginIdentifier",
                "passwordHash")
            VALUES(@Id,
                @Login,
                @Normalized,
                'test-no-login');
            INSERT INTO "identity"."customerProfiles"("userId",
                "displayName",
                "maskedEmail",
                "maskedPhone")
            VALUES((
                    SELECT "resolved1"."id"
                    FROM "identity"."users" AS "resolved1"
                    WHERE "resolved1"."publicId" = @Id),
                'Qualified Staff',
                'staff***',
                '***');
            INSERT INTO "identity"."userRoles"("userId",
                "roleId")
            SELECT(
                SELECT "resolved2"."id"
                FROM "identity"."users" AS "resolved2"
                WHERE "resolved2"."publicId" = @Id),
            "roles"."id"
            FROM "identity"."roles" AS "roles"
            WHERE "roles"."code" = @Role;
            INSERT INTO "support"."staffCapacity"("userId",
                "availability",
                "capacity")
            VALUES((
                    SELECT "resolved3"."id"
                    FROM "identity"."users" AS "resolved3"
                    WHERE "resolved3"."publicId" = @Id),
                'Available',
                @Capacity)
            """,
            new
            {
                Id = id,
                Login = id + "@verification.invalid",
                Normalized = (id + "@verification.invalid").ToUpperInvariant(),
                Role = role,
                Capacity = capacity
            });
        return id;
    }

    [Fact]
    public async Task Support_concerns_route_only_to_eligible_teams_and_closed_chats_are_terminal_with_independent_financial_cases()
    {
        var ct = CancellationToken.None;
        await using var c = await _connections.OpenConnection(ct);
        await c.ExecuteAsync(
            """
            INSERT INTO "identity"."userRoles"("userId",
                "roleId")
            SELECT(
                SELECT "resolved1"."id"
                FROM "identity"."users" AS "resolved1"
                WHERE "resolved1"."publicId" = @Admin),
            "roles"."id"
            FROM "identity"."roles" AS "roles"
            WHERE "roles"."code" = 'super_admin';
            INSERT INTO "support"."staffCapacity"("userId",
                "availability",
                "capacity")
            VALUES((
                    SELECT "resolved2"."id"
                    FROM "identity"."users" AS "resolved2"
                    WHERE "resolved2"."publicId" = @Admin),
                'Available',
                10)
            """,
            new { Admin = OtherCustomer });
        var concerns = await _repository.GetConcerns(ct);
        Assert.Contains(concerns, item => item.Code == "Fraud");
        Assert.Equal(concerns.Count, concerns.Select(item => item.Code).Distinct().Count());
        var general = await _repository.SendMessage(
            Customer,
            new SupportSendRequest(
                null,
                null,
                "Account help",
                "Please help me with my account.",
                ClientMessageId: Guid.NewGuid(),
                ConcernCode: "Account"),
            ct);
        var queued = await _repository.GetSupportThread(Customer, general, ct);
        Assert.NotNull(queued);
        Assert.Equal("Open", queued.Status);
        Assert.True(queued.CanClose);
        Assert.True(queued.CanReply);
        Assert.Null(queued.QueueType);
        Assert.Null(queued.AssignedTo);
        Assert.Null(await _repository.GetSupportThread(Agent, general, ct));
        await Assert.ThrowsAsync<PortalNotFoundException>(() => _repository.GetMessages(Agent, general, ct));
        var paymentStaff = await AddSupportStaff("payments_specialist");
        Assert.Null(await _repository.GetSupportThread(paymentStaff, general, ct));
        var generalStaff = await AddSupportStaff("dispute_agent");
        await _repository.AssignWaitingSupportConversations(ct);
        var assigned = await _repository.GetSupportThread(generalStaff, general, ct);
        Assert.NotNull(assigned);
        Assert.Equal(generalStaff, assigned.AssignedTo);
        Assert.True(assigned.CanReply);
        Assert.False(assigned.CanAssign);
        var otherGeneral = await AddSupportStaff("dispute_agent");
        await Assert.ThrowsAsync<PortalForbiddenException>(() => _repository.AssignConversation(
            generalStaff,
            general,
            new AssignmentRequest(assigned.Version, otherGeneral, "Unauthorized reassignment attempt."),
            ct));
        var adminView = (await _repository.GetSupportThread(OtherCustomer, general, ct))!;
        await _repository.AssignConversation(
            OtherCustomer,
            general,
            new AssignmentRequest(adminView.Version, otherGeneral, "Authorized handover within the general support team."),
            ct);
        Assert.Null(await _repository.GetSupportThread(generalStaff, general, ct));
        await Assert.ThrowsAsync<PortalNotFoundException>(() => _repository.SendMessage(
            generalStaff,
            new SupportSendRequest(general, null, "Account help", "An unauthorized late reply.", ClientMessageId: Guid.NewGuid()),
            ct));
        var current = (await _repository.GetSupportThread(Customer, general, ct))!;
        var closed = await _repository.CloseConversation(Customer, general, new CloseSupportRequest(current.Version), ct);
        Assert.Equal("Closed", closed.Status);
        Assert.NotNull(closed.ClosedAt);
        Assert.False(closed.CanReply);
        Assert.False(closed.CanClose);
        Assert.Equal(
            "Closed",
            (await _repository.CloseConversation(Customer, general, new CloseSupportRequest(current.Version), ct)).Status);
        Assert.Contains(await _repository.GetThreads(Customer, ct), item => item.Id == general);
        Assert.DoesNotContain(
            await _repository.GetThreads(Customer, ct, filter: new HistoryFilter { Status = "active" }),
            item => item.Id == general);
        Assert.Contains(
            await _repository.GetThreads(Customer, ct, filter: new HistoryFilter { Status = "closed" }),
            item => item.Id == general);
        Assert.Single(await _repository.GetMessages(Customer, general, ct));
        await Assert.ThrowsAsync<PortalConflictException>(() => _repository.SendMessage(
            Customer,
            new SupportSendRequest(general, null, "Account help", "This must not be sent after closure.", ClientMessageId: Guid.NewGuid()),
            ct));
        await Assert.ThrowsAnyAsync<DbException>(() => c.ExecuteAsync(
            """
            UPDATE "support"."conversations"
            SET "conversationStatus" = 'Open',
            "closedAt" = NULL
            WHERE "conversations"."publicId" = @Id
            """,
            new { Id = general }));
        await Assert.ThrowsAnyAsync<DbException>(() => c.ExecuteAsync(
            """
            INSERT INTO "support"."messages"("publicId",
                "threadId",
                "authorId",
                "body",
                "clientMessageId",
                "requestSubject")
            VALUES(@Id,
                (
                    SELECT "resolved1"."id"
                    FROM "support"."conversations" AS "resolved1"
                    WHERE "resolved1"."publicId" = @Thread),
                (
                    SELECT "resolved2"."id"
                    FROM "identity"."users" AS "resolved2"
                    WHERE "resolved2"."publicId" = @Actor),
                'Rejected closed-chat write',
                @Request,
                'Account help')
            """,
            new
            {
                Id = Guid.NewGuid(),
                Thread = general,
                Actor = Customer,
                Request = Guid.NewGuid()
            }));
        Assert.Single(await _repository.GetMessages(Customer, general, ct));
        var fraud = await _repository.SendMessage(
            Customer,
            new SupportSendRequest(
                null,
                null,
                "Suspicious activity",
                "Please review this suspicious activity.",
                ClientMessageId: Guid.NewGuid(),
                ConcernCode: "Fraud"),
            ct);
        Assert.NotNull(await _repository.GetSupportThread(Agent, fraud, ct));
        Assert.Null(await _repository.GetSupportThread(paymentStaff, fraud, ct));
        Assert.Null(await _repository.GetSupportThread(otherGeneral, fraud, ct));
        var fraudView = (await _repository.GetSupportThread(Agent, fraud, ct))!;
        await _repository.EscalateConversation(
            Agent,
            fraud,
            new EscalationRequest(fraudView.Version, "Senior fraud investigation is required."),
            ct);
        Assert.Null(await _repository.GetSupportThread(Agent, fraud, ct));
        Assert.NotNull(await _repository.GetSupportThread(OtherCustomer, fraud, ct));
        var escalated = (await _repository.GetSupportThread(OtherCustomer, fraud, ct))!;
        await Assert.ThrowsAsync<PortalForbiddenException>(() => _repository.AssignConversation(
            OtherCustomer,
            fraud,
            new AssignmentRequest(escalated.Version, Agent, "A non-supervisor must not receive an escalated enquiry."),
            ct));
        await _repository.AssignConversation(
            OtherCustomer,
            fraud,
            new AssignmentRequest(escalated.Version, OtherCustomer, "Explicit supervisor assignment for senior review."),
            ct);
        await _repository.CloseConversation(
            OtherCustomer,
            fraud,
            new CloseSupportRequest(
                (await _repository.GetSupportThread(OtherCustomer, fraud, ct))!.Version,
                "Senior review of this conversation is complete."),
            ct);
        var financial = await _repository.CreateDispute(
            Customer,
            new CreateDisputeCommand
            {
                TransactionId = Transaction,
                Category = "Unrecognized",
                Reason = "I did not make this transaction",
                Description = "Please investigate this transaction independently."
            },
            "chat-financial-independence",
            ct);
        var chat = await _repository.SendMessage(
            Customer,
            new SupportSendRequest(
                null,
                financial.Id,
                "Case question",
                "Please confirm the next review step.",
                ClientMessageId: Guid.NewGuid()),
            ct);
        var chatView = (await _repository.GetSupportThread(Customer, chat, ct))!;
        Assert.False(chatView.CanClose);
        await Assert.ThrowsAsync<PortalConflictException>(() => _repository.CloseConversation(
            Customer,
            chat,
            new CloseSupportRequest(chatView.Version),
            ct));
        Assert.Equal("Submitted", (await _repository.GetCustomerDispute(Customer, financial.Id, ct))!.Dispute.Status);
        var newChat = await _repository.SendMessage(
            Customer,
            new SupportSendRequest(
                null,
                financial.Id,
                "New question",
                "I need further help with this case.",
                ClientMessageId: Guid.NewGuid()),
            ct);
        Assert.Equal(chat, newChat);
        Assert.Equal("Open", (await _repository.GetSupportThread(Customer, chat, ct))!.Status);
        var notifications = new NotificationRepository(
            _connections,
            DatabaseDialect.GetDatabaseDialect(_provider),
            new TestProtector(),
            _repository,
            _repository, DisputePortal.TestFixtures.TestRuntimeConfiguration.Read());
        var claims = await notifications.ClaimNotifications(ct);
        Assert.DoesNotContain(claims, item => item.UserId == generalStaff && item.Milestone == "Event:SupportAssigned");
        Assert.True(await c.ExecuteScalarAsync<int>("""
            SELECT COUNT(*)
            FROM "audit"."auditEvents" AS "admin_audit_logs"
            WHERE "admin_audit_logs"."action" = 'SupportConversationClosed'
            """) >= 2);
    }

    [Fact]
    public async Task Concurrent_close_and_send_commit_no_messages_after_terminal_closure()
    {
        var ct = CancellationToken.None;
        var id = await _repository.SendMessage(
            Customer,
            new SupportSendRequest(null, null, "General enquiry", "Please help me with this enquiry.", ClientMessageId: Guid.NewGuid()),
            ct);
        var row = (await _repository.GetSupportThread(Customer, id, ct))!;
        async Task<bool> TrySend()
        {
            try
            {
                await _repository.SendMessage(
                    Customer,
                    new SupportSendRequest(id, null, "General enquiry", "A message racing with closure.", ClientMessageId: Guid.NewGuid()),
                    ct);
                return true;
            }
            catch (PortalConflictException)
            {
                return false;
            }
        }

        var sending = TrySend();
        var closing = _repository.CloseConversation(Customer, id, new CloseSupportRequest(row.Version), ct);
        await Task.WhenAll(sending, closing);
        var closedResult = await closing;
        var sentResult = await sending;
        Assert.Equal("Closed", closedResult.Status);
        var count = (await _repository.GetMessages(Customer, id, ct)).Count;
        Assert.Equal(sentResult ? 2 : 1, count);
        await Assert.ThrowsAsync<PortalConflictException>(() => _repository.SendMessage(
            Customer,
            new SupportSendRequest(id, null, "General enquiry", "No messages may be appended now.", ClientMessageId: Guid.NewGuid()),
            ct));
        Assert.Equal(count, (await _repository.GetMessages(Customer, id, ct)).Count);
    }
}
