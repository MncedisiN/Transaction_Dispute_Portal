using Dapper;
using DisputePortal.Api.Models.Exceptions;
using DisputePortal.Api.Models.Requests;
using DisputePortal.Api.Service.Disputes;
using Xunit;
using DisputePortal.Api.Models.Domain;

namespace DisputePortal.IntegrationTests;

public sealed partial class DisputeWorkflowRepositoryTests
{
    private async Task<(Guid Id, Guid Thread)> ClosedSupportCase(string status = "Withdrawn")
    {
        var ct = CancellationToken.None;
        var created = await _repository.CreateDispute(
            Customer,
            new CreateDisputeCommand
            {
                TransactionId = Transaction,
                Category = "Unrecognized",
                Reason = "Not my payment",
                Description = "Please investigate this payment."
            },
            "support-closed-" + Guid.NewGuid(),
            ct);
        var thread = await _repository.SendMessage(
            Customer,
            new SupportSendRequest(null, created.Id, "Payment question", "Please review this payment.", Transaction, Guid.NewGuid()),
            ct);
        var detail = (await _repository.GetCustomerDispute(Customer, created.Id, ct))!;
        if (status == "Withdrawn")
        {
            await _repository.WithdrawCustomerDispute(Customer, created.Id, detail.Dispute.Version, "support-close", ct);
        }
        else
        {
            var version = await _repository.TransitionDispute(
                Agent,
                created.Id,
                new TransitionCommand(detail.Dispute.Version, "UnderReview", "Review started."),
                "support-review",
                ct);
            await _repository.TransitionDispute(
                Agent,
                created.Id,
                new TransitionCommand(version, status, "The case review has been completed."),
                "support-close",
                ct);
        }

        return (created.Id, thread);
    }

    [Theory]
    [InlineData("Resolved")]
    [InlineData("Declined")]
    [InlineData("Withdrawn")]
    public async Task Closed_case_history_remains_read_only_while_its_related_general_enquiry_accepts_replies(string status)
    {
        var ct = CancellationToken.None;
        var closed = await ClosedSupportCase(status);
        await using var c = await _connections.OpenConnection(ct);
        await c.ExecuteAsync(
            """
      INSERT INTO "identity"."userRoles"("userId",
          "roleId")
      SELECT(
          SELECT "resolved1"."id"
          FROM "identity"."users" AS "resolved1"
          WHERE "resolved1"."publicId" = @Agent),
      "roles"."id"
      FROM "identity"."roles" AS "roles"
      WHERE "roles"."code" = 'dispute_agent'
      """,
            new { Agent });
        var request = new SupportSendRequest(
            null,
            null,
            "Enquiry about my closed case",
            "Please explain the decision on my case.",
            ClientMessageId: Guid.NewGuid(),
            RelatedDisputeId: closed.Id);
        var general = await _repository.SendMessage(Customer, request, ct);
        Assert.NotEqual(closed.Thread, general);
        Assert.Equal(general, await _repository.SendMessage(Customer, request, ct));
        var info = Assert.Single(await _repository.GetThreads(Customer, ct, relatedDisputeId: closed.Id));
        Assert.Equal(closed.Id, info.RelatedDisputeId);
        Assert.Null(info.DisputeId);
        Assert.Equal(Transaction, info.TransactionId);
        Assert.Null(info.CaseStatus);
        Assert.Contains(status, info.RelatedCaseContext);
        Assert.Contains("100.00 ZAR", info.RelatedCaseContext);
        var notesBefore = (await _repository.GetCustomerDispute(Customer, closed.Id, ct))!.Notes.Count;
        await _repository.SendMessage(
            Agent,
            new SupportSendRequest(general, null, request.Subject, "We can explain the completed review.", ClientMessageId: Guid.NewGuid()),
            ct);
        Assert.Equal(2, (await _repository.GetMessages(Customer, general, ct)).Count);
        Assert.Equal(notesBefore, (await _repository.GetCustomerDispute(Customer, closed.Id, ct))!.Notes.Count);
        await Assert.ThrowsAsync<PortalConflictException>(() => _repository.SendMessage(
            Customer,
            new SupportSendRequest(
                closed.Thread,
                closed.Id,
                "Payment question",
                "Do not reopen the closed conversation.",
                Transaction,
                Guid.NewGuid()),
            ct));
        await Assert.ThrowsAsync<PortalConflictException>(() => _repository.SendMessage(
            Agent,
            new SupportSendRequest(
                closed.Thread,
                closed.Id,
                "Payment question",
                "Do not reopen the closed conversation.",
                Transaction,
                Guid.NewGuid()),
            ct));
        Assert.Single(await _repository.GetMessages(Customer, closed.Thread, ct));
        Assert.Equal(
            0,
            await c.ExecuteScalarAsync<int>(
                """
      SELECT COUNT(*)
      FROM "notifications"."events" n
      JOIN "support"."messages" m
      ON "m"."publicId" = "n"."eventKey"
      JOIN "identity"."users" u
      ON "u"."id" = "n"."userId"
      WHERE(
          SELECT "related1"."publicId"
          FROM "support"."conversations" AS "related1"
          WHERE "related1"."id" = "m"."threadId") = @Thread
      AND "m"."authorId" = "u"."id"
      """,
                new { Thread = general }));
        Assert.Equal(
            2,
            await c.ExecuteScalarAsync<int>(
                """
      SELECT COUNT(*)
      FROM "notifications"."events" n
      JOIN "support"."messages" m
      ON "m"."publicId" = "n"."eventKey"
      WHERE(
          SELECT "related1"."publicId"
          FROM "support"."conversations" AS "related1"
          WHERE "related1"."id" = "m"."threadId") = @Thread
      AND "n"."disputeId" IS NULL
      AND "n"."url" = @Url
      """,
                new { Thread = general, Url = "/support?thread=" + general }));
    }

    [Fact]
    public async Task Related_enquiry_creation_and_idempotency_cannot_change_case_ownership_or_content()
    {
        var ct = CancellationToken.None;
        var closed = await ClosedSupportCase();
        var request = new SupportSendRequest(
            null,
            null,
            "Closed case question",
            "Please explain this closed case.",
            ClientMessageId: Guid.NewGuid(),
            RelatedDisputeId: closed.Id);
        await Assert.ThrowsAsync<PortalNotFoundException>(() => _repository.SendMessage(OtherCustomer, request, ct));
        var general = await _repository.SendMessage(Customer, request, ct);
        await Assert.ThrowsAsync<PortalConflictException>(() => _repository.SendMessage(
            Customer,
            new SupportSendRequest(request) { Body = "Different message with the same request." },
            ct));
        await Assert.ThrowsAsync<PortalConflictException>(() => _repository.SendMessage(
            Customer,
            new SupportSendRequest(request) { RelatedDisputeId = Guid.NewGuid() },
            ct));
        await Assert.ThrowsAsync<PortalValidationException>(() => _repository.SendMessage(
            Customer,
            new SupportSendRequest(request)
            {
                DisputeId = closed.Id,
                ClientMessageId = Guid.NewGuid()
            },
            ct));
        await Assert.ThrowsAsync<PortalNotFoundException>(() => _repository.SendMessage(
            OtherCustomer,
            new SupportSendRequest(general, null, request.Subject, "This is not my conversation.", ClientMessageId: Guid.NewGuid()),
            ct));
        Assert.Single(await _repository.GetMessages(Customer, general, ct));
    }

    [Fact]
    public async Task Active_case_uses_its_case_conversation_instead_of_a_related_general_enquiry()
    {
        var ct = CancellationToken.None;
        var created = await _repository.CreateDispute(
            Customer,
            new CreateDisputeCommand
            {
                TransactionId = Transaction,
                Category = "Unrecognized",
                Reason = "Not my payment",
                Description = "Please investigate this payment."
            },
            "support-active",
            ct);
        await Assert.ThrowsAsync<PortalConflictException>(() => _repository.SendMessage(
            Customer,
            new SupportSendRequest(null, null, "Payment question", "Please review this open case.", RelatedDisputeId: created.Id),
            ct));
    }

    [Fact]
    public async Task Transaction_conversations_select_the_latest_reinstated_case_and_preserve_the_old_case_thread()
    {
        var ct = CancellationToken.None;
        var reviewed = await ReviewableRefundCase();
        var finance = Finance();
        await FundRecallAccount();
        var oldThread = await _repository.SendMessage(
            Customer,
            new SupportSendRequest(null, reviewed.Id, "Payment question", "Please review this payment.", Transaction, Guid.NewGuid()),
            ct);
        await finance.ApproveRefund(
            Agent,
            reviewed.Id,
            new(reviewed.Version, Guid.NewGuid(), "Approve the original payment refund."),
            "support-refund",
            ct);
        var parent = (await _repository.GetCustomerDispute(Customer, reviewed.Id, ct))!;
        await finance.RecallRefund(
            Agent,
            reviewed.Id,
            new(parent.Dispute.Version, Guid.NewGuid(), "Recall the refund for another review."),
            "support-recall",
            ct);
        var notice = Assert.Single((await _repository.GetNotifications(Customer, 1, 100, ct)).Items, n => n.Type == "RefundRecalled");
        Assert.Contains("100.00 ZAR", notice.Body);
        Assert.Contains("••••", notice.Body);
        Assert.Contains("recalled from", notice.Body);
        Assert.DoesNotContain("Recall the refund for another review.", notice.Body);
        parent = (await _repository.GetCustomerDispute(Customer, reviewed.Id, ct))!;
        var child = await _repository.ReinstateCustomerDispute(
            Customer,
            reviewed.Id,
            new(parent.Dispute.Version, Guid.NewGuid(), "Please review the new information."),
            "support-reinstate",
            ct);
        var nextThread = await _repository.SendMessage(
            Customer,
            new SupportSendRequest(null, null, "New payment review", "Please review my reinstated case.", Transaction, Guid.NewGuid()),
            ct);
        Assert.NotEqual(oldThread, nextThread);
        var info = await _repository.GetSupportThread(Customer, nextThread, ct);
        Assert.Equal(child.Id, info!.DisputeId);
        await Assert.ThrowsAsync<PortalConflictException>(() => _repository.SendMessage(
            Customer,
            new SupportSendRequest(
                oldThread,
                reviewed.Id,
                "Payment question",
                "The old case thread stays closed.",
                Transaction,
                Guid.NewGuid()),
            ct));
        Assert.Single(await _repository.GetMessages(Customer, oldThread, ct));
    }
}
