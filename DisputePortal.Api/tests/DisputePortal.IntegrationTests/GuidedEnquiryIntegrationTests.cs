using Dapper;
using DisputePortal.Api.Models.Domain;
using DisputePortal.Api.Models.Exceptions;
using DisputePortal.Api.Models.Requests;
using DisputePortal.Api.Service.Disputes;
using Xunit;

namespace DisputePortal.IntegrationTests;

public sealed partial class DisputeWorkflowRepositoryTests
{
    private static SupportSendRequest Guided(string concern = "General", Guid? related = null, Guid? transaction = null)
    {
        var details = new EnquiryDetails
        {
            Description = "Please explain the result and the next steps.",
            AdditionalDetails = "I would like a clear written response."
        };
        return new(
            null,
            null,
            "Reviewed Enquiry",
            details.MessageBody,
            TransactionId: transaction,
            ClientMessageId: Guid.NewGuid(),
            RelatedDisputeId: related,
            ConcernCode: concern,
            Enquiry: details,
            StartNewConversation: related.HasValue);
    }

    [Fact]
    public async Task Guided_enquiry_submission_is_atomic_and_idempotent_with_structured_answers_and_routing()
    {
        var ct = CancellationToken.None;
        var request = Guided();
        var ids = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => _repository.SendMessage(Customer, request, ct)));
        Assert.Single(ids.Distinct());
        var id = ids[0];
        var message = Assert.Single(await _repository.GetMessages(Customer, id, ct));
        Assert.NotNull(message.Enquiry);
        Assert.Equal("General", message.Enquiry.ConcernCode);
        Assert.Equal(request.Enquiry!.Description, message.Enquiry.Description);
        Assert.Equal(request.Enquiry.AdditionalDetails, message.Enquiry.AdditionalDetails);
        Assert.Equal(request.Body, message.Body);
        await using var c = await _connections.OpenConnection(ct);
        Assert.Equal(
            1,
            await c.ExecuteScalarAsync<int>(
                """
            SELECT COUNT(*)
            FROM "support"."messages" AS "support_messages"
            WHERE "support_messages"."clientMessageId" = @Id
            """,
                new { Id = request.ClientMessageId }));
        Assert.Equal(
            1,
            await c.ExecuteScalarAsync<int>(
                """
            SELECT COUNT(*)
            FROM "audit"."auditEvents" AS "admin_audit_logs"
            WHERE "admin_audit_logs"."action" = 'SupportEnquirySubmitted'
            AND "admin_audit_logs"."entityPublicId" = @Id
            """,
                new { Id = id }));
        var general = await AddSupportStaff("dispute_agent");
        await _repository.AssignWaitingSupportConversations(ct);
        var assigned = (await _repository.GetSupportThread(general, id, ct))!;
        Assert.True(assigned.CanReply);
        Assert.Equal(general, assigned.AssignedTo);
        Assert.Null(await _repository.GetSupportThread(Agent, id, ct));
        await Assert.ThrowsAsync<PortalNotFoundException>(() => _repository.GetMessages(OtherCustomer, id, ct));
        var changed = new SupportSendRequest(request)
        {
            Enquiry = new global::DisputePortal.Api.Models.Domain.EnquiryDetails(request.Enquiry)
            {
                OccurredOn = new DateOnly(2026, 1, 2)
            }
        };
        await Assert.ThrowsAsync<PortalConflictException>(() => _repository.SendMessage(Customer, changed, ct));
    }

    [Fact]
    public async Task Guided_closed_case_followups_create_fresh_conversations_without_reopening_or_exposing_old_history()
    {
        var ct = CancellationToken.None;
        var closed = await ClosedSupportCase();
        var generalStaff = await AddSupportStaff("dispute_agent");
        var existing = await _repository.SendMessage(
            Customer,
            new(
                null,
                null,
                "Existing related enquiry",
                "Please explain the previous review.",
                ClientMessageId: Guid.NewGuid(),
                RelatedDisputeId: closed.Id),
            ct);
        var firstRequest = Guided(related: closed.Id);
        var first = await _repository.SendMessage(Customer, firstRequest, ct);
        var second = await _repository.SendMessage(Customer, Guided(related: closed.Id), ct);
        Assert.NotEqual(existing, first);
        Assert.NotEqual(first, second);
        Assert.NotEqual(closed.Thread, first);
        Assert.Single(await _repository.GetMessages(Customer, closed.Thread, ct));
        Assert.Equal("Withdrawn", (await _repository.GetCustomerDispute(Customer, closed.Id, ct))!.Dispute.Status);
        Assert.Equal(closed.Id, (await _repository.GetSupportThread(Customer, first, ct))!.RelatedDisputeId);
        Assert.Null(await _repository.GetSupportThread(generalStaff, first, ct));
        await Assert.ThrowsAsync<PortalNotFoundException>(() => _repository.GetMessages(generalStaff, first, ct));
        Assert.NotNull(await _repository.GetSupportThread(Agent, first, ct));
        await Assert.ThrowsAsync<PortalNotFoundException>(() => _repository.SendMessage(OtherCustomer, Guided(related: closed.Id), ct));
        var info = (await _repository.GetSupportThread(Customer, first, ct))!;
        await _repository.CloseConversation(Customer, first, new(info.Version), ct);
        Assert.Equal(first, await _repository.SendMessage(Customer, firstRequest, ct));
        Assert.Single(await _repository.GetMessages(Customer, first, ct));
        await Assert.ThrowsAsync<PortalConflictException>(() => _repository.SendMessage(
            Customer,
            new(first, null, "Reviewed Enquiry", "This closed thread must remain read-only.", ClientMessageId: Guid.NewGuid()),
            ct));
        await Assert.ThrowsAsync<PortalConflictException>(() => _repository.SendMessage(
            Customer,
            new(closed.Thread, closed.Id, "Old Case", "The original case must not reopen.", ClientMessageId: Guid.NewGuid()),
            ct));
    }

    [Fact]
    public async Task Guided_required_context_and_owned_records_are_enforced_before_enquiry_creation()
    {
        var ct = CancellationToken.None;
        var concerns = await _repository.GetConcerns(ct);
        Assert.Equal("Transaction", concerns.Single(c => c.Code == "Dispute").ContextKind);
        Assert.True(concerns.Single(c => c.Code == "Technical").CollectDate);
        await Assert.ThrowsAsync<PortalValidationException>(() => _repository.SendMessage(Customer, Guided("Dispute"), ct));
        await Assert.ThrowsAsync<PortalValidationException>(() => _repository.SendMessage(Customer, Guided("Refund"), ct));
        await Assert.ThrowsAsync<PortalNotFoundException>(() => _repository.SendMessage(
            Customer,
            Guided("Dispute", transaction: OtherTransaction),
            ct));
        await Assert.ThrowsAsync<PortalForbiddenException>(() => _repository.SendMessage(Agent, Guided(), ct));
        var foreign = await _repository.CreateDispute(
            OtherCustomer,
            new CreateDisputeCommand
            {
                TransactionId = OtherTransaction,
                Category = "Other",
                Reason = "Other",
                Description = "Please investigate this account transaction."
            },
            "guided-other-case",
            ct);
        await Assert.ThrowsAsync<PortalNotFoundException>(() => _repository.SendMessage(Customer, Guided(related: foreign.Id), ct));
        var own = await _repository.SendMessage(Customer, Guided("Dispute", transaction: Transaction), ct);
        Assert.Equal(Transaction, (await _repository.GetSupportThread(Customer, own, ct))!.TransactionId);
        Assert.Single(await _repository.GetMessages(Customer, own, ct));
        var financial = await _repository.CreateDispute(
            Customer,
            new CreateDisputeCommand
            {
                TransactionId = Transaction,
                Category = "Unrecognized",
                Reason = "Not my transaction",
                Description = "Please investigate this transaction."
            },
            "guided-active-case",
            ct);
        var activeRequest = new SupportSendRequest(Guided("Refund"))
        {
            DisputeId = financial.Id
        };
        var same = await _repository.SendMessage(Customer, activeRequest, ct);
        Assert.Equal(own, same);
        var active = (await _repository.GetSupportThread(Customer, same, ct))!;
        Assert.False(active.CanClose);
        Assert.Equal(financial.Id, active.DisputeId);
        Assert.NotNull(await _repository.GetSupportThread(Agent, same, ct));
    }
}
