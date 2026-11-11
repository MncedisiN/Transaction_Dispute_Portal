using DisputePortal.Api.Models.Domain;
using Dapper;
using DisputePortal.Api.Models.Exceptions;
using DisputePortal.Api.Service.Disputes;
using Xunit;

namespace DisputePortal.IntegrationTests;

public sealed partial class DisputeWorkflowRepositoryTests
{
    [Fact]
    public async Task Transaction_intake_returns_the_existing_active_case_before_accepting_another_submission()
    {
        var ct = CancellationToken.None;
        var available = await _repository.AssessDispute(Customer, Transaction, ct);
        Assert.True(available.CanStart);
        Assert.Null(available.ActiveDisputeId);
        var command = new CreateDisputeCommand
        {
            TransactionId = Transaction,
            Category = "Other",
            Reason = "Other",
            Description = "Please investigate this original payment."
        };
        var created = await _repository.CreateDispute(Customer, command, "intake-active-original", ct);
        var existing = await _repository.AssessDispute(Customer, Transaction, ct);
        Assert.False(existing.CanStart);
        Assert.False(existing.Allowed);
        Assert.False(existing.AssistedReview);
        Assert.Equal(created.Id, existing.ActiveDisputeId);
        Assert.Contains("existing dispute", existing.BlockReason);
        await Assert.ThrowsAsync<PortalConflictException>(() => _repository.CreateDispute(Customer, command, "intake-active-second", ct));
        Assert.Equal(created.Id, (await _repository.CreateDispute(Customer, command, "intake-active-original", ct)).Id);
        await Assert.ThrowsAsync<PortalNotFoundException>(() => _repository.AssessDispute(OtherCustomer, Transaction, ct));
    }

    [Fact]
    public async Task Withdrawn_case_allows_a_new_eligible_case_without_replacing_the_original_payment()
    {
        var ct = CancellationToken.None;
        var original = (await _repository.GetTransaction(Customer, Transaction, ct))!;
        var command = new CreateDisputeCommand
        {
            TransactionId = Transaction,
            Category = "Other",
            Reason = "Other",
            Description = "Please investigate the original merchant payment."
        };
        var first = await _repository.CreateDispute(Customer, command, "intake-withdraw-first", ct);
        var detail = (await _repository.GetCustomerDispute(Customer, first.Id, ct))!;
        await _repository.WithdrawCustomerDispute(Customer, first.Id, detail.Dispute.Version, "intake-withdraw", ct);
        var ready = await _repository.AssessDispute(Customer, Transaction, ct);
        Assert.True(ready.CanStart);
        Assert.Null(ready.ActiveDisputeId);
        Assert.False(ready.HasPendingFinancialOperation);
        Assert.False(ready.HasPostedRefund);
        var next = await _repository.CreateDispute(Customer, command, "intake-withdraw-next", ct);
        Assert.NotEqual(first.Id, next.Id);
        Assert.Equal("Withdrawn", (await _repository.GetCustomerDispute(Customer, first.Id, ct))!.Dispute.Status);
        var payment = (await _repository.GetTransaction(Customer, Transaction, ct))!;
        Assert.Equal(
            (original.Id, original.Amount, original.Currency, original.TransactionAt, original.Status, original.Description),
            (payment.Id, payment.Amount, payment.Currency, payment.TransactionAt, payment.Status, payment.Description));
        Assert.Equal("Posted", payment.Status);
        await using var c = await _connections.OpenConnection(ct);
        Assert.Equal(
            1,
            await c.ExecuteScalarAsync<int>(
                """
            SELECT COUNT(*)
            FROM "transactions"."transactions"
            WHERE "publicId" = @Id
            """,
                new { Id = Transaction }));
        Assert.Equal(next.Id, (await _repository.AssessDispute(Customer, Transaction, ct)).ActiveDisputeId);
    }

    [Fact]
    public async Task Closed_case_with_a_pending_financial_command_blocks_a_new_case_and_specialist_bypass()
    {
        var ct = CancellationToken.None;
        var command = new CreateDisputeCommand
        {
            TransactionId = Transaction,
            Category = "Other",
            Reason = "Other",
            Description = "Please investigate the original merchant payment."
        };
        var created = await _repository.CreateDispute(Customer, command, "intake-pending-first", ct);
        var detail = (await _repository.GetCustomerDispute(Customer, created.Id, ct))!;
        var version = await _repository.WithdrawCustomerDispute(Customer, created.Id, detail.Dispute.Version, "intake-pending-close", ct);
        await using var c = await _connections.OpenConnection(ct);
        await c.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO "disputes"."financialCommands" (
                "publicId",
                "requestId",
                "disputeId",
                "originalTransactionId",
                "accountId",
                "customerId",
                "kind",
                "amount",
                "currencyCode",
                "reason",
                "expectedVersion",
                "correlation",
                "state")
            SELECT @Command,
                @Request,
                d."id",
                t."id",
                t."accountId",
                d."customerId",
                'Refund',
                t."amount",
                t."currencyCode",
                'Prior financial operation awaiting confirmation.',
                @Version,
                @Correlation,
                'Queued'
            FROM "disputes"."disputes" d
            JOIN "transactions"."transactions" t
                ON t."id" = d."transactionId"
            WHERE d."publicId" = @Dispute
            """,
            new
            {
                Command = Guid.NewGuid(),
                Request = Guid.NewGuid(),
                Correlation = Guid.NewGuid(),
                Version = version,
                Dispute = created.Id
            },
            cancellationToken: ct));
        var pending = await _repository.AssessDispute(Customer, Transaction, ct);
        Assert.False(pending.CanStart);
        Assert.Null(pending.ActiveDisputeId);
        Assert.True(pending.HasPendingFinancialOperation);
        Assert.False(pending.HasPostedRefund);
        Assert.Contains("processing", pending.BlockReason);
        await Assert.ThrowsAsync<PortalConflictException>(() => _repository.CreateDispute(Customer, command, "intake-pending-next", ct));
        await Assert.ThrowsAsync<PortalConflictException>(() => _repository.CreateDispute(
            Customer,
            new global::DisputePortal.Api.Models.Domain.CreateDisputeCommand(command) { RequestSpecialistReview = true },
            "intake-pending-manual",
            ct));
        Assert.Equal("Withdrawn", (await _repository.GetCustomerDispute(Customer, created.Id, ct))!.Dispute.Status);
    }

    [Fact]
    public async Task Confirmed_refund_blocks_a_new_case_until_the_existing_refund_is_actually_recalled()
    {
        var ct = CancellationToken.None;
        var reviewed = await ReviewableRefundCase();
        var finance = Finance();
        await FundRecallAccount();
        await finance.ApproveRefund(
            Agent,
            reviewed.Id,
            new(reviewed.Version, Guid.NewGuid(), "Original payment reviewed and refund authorised."),
            "intake-refund",
            ct);
        var refunded = await _repository.AssessDispute(Customer, Transaction, ct);
        Assert.False(refunded.CanStart);
        Assert.Null(refunded.ActiveDisputeId);
        Assert.True(refunded.HasPostedRefund);
        await Assert.ThrowsAsync<PortalConflictException>(() => _repository.CreateDispute(
            Customer,
            new CreateDisputeCommand
            {
                TransactionId = Transaction,
                Category = "Other",
                Reason = "Other",
                Description = "A new case must not duplicate a completed refund."
            },
            "intake-refund-next",
            ct));
        var completed = (await _repository.GetCustomerDispute(Customer, reviewed.Id, ct))!.Dispute;
        await finance.RecallRefund(
            Agent,
            reviewed.Id,
            new(completed.Version, Guid.NewGuid(), "Refund recalled after reviewing new merchant evidence."),
            "intake-recall",
            ct);
        var ready = await _repository.AssessDispute(Customer, Transaction, ct);
        Assert.True(ready.CanStart);
        Assert.False(ready.HasPostedRefund);
        Assert.False(ready.HasPendingFinancialOperation);
    }
}
