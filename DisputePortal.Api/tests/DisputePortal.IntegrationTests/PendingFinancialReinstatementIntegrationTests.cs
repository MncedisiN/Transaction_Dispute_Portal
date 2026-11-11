using Dapper;
using DisputePortal.Api.Models.Exceptions;
using DisputePortal.Api.Models.Requests;
using Xunit;

namespace DisputePortal.IntegrationTests;

public sealed partial class DisputeWorkflowRepositoryTests
{
    [Fact]
    public async Task Recalled_parent_cannot_be_reinstated_while_the_same_original_payment_has_unfinished_financial_work()
    {
        var ct = CancellationToken.None;
        var reviewed = await ReviewableRefundCase();
        var finance = Finance();
        await FundRecallAccount();
        await finance.ApproveRefund(
            Agent,
            reviewed.Id,
            new(reviewed.Version, Guid.NewGuid(), "Approve the original verified merchant payment refund."),
            "pending-reinstate-refund",
            ct);
        var parent = (await _repository.GetCustomerDispute(Customer, reviewed.Id, ct))!.Dispute;
        await finance.RecallRefund(
            Agent,
            reviewed.Id,
            new(parent.Version, Guid.NewGuid(), "Recall the verified refund for further merchant review."),
            "pending-reinstate-recall",
            ct);
        parent = (await _repository.GetCustomerDispute(Customer, reviewed.Id, ct))!.Dispute;
        var request = new ReinstateDisputeRequest(
            parent.Version,
            Guid.NewGuid(),
            "Review the original merchant payment after its refund recall.");
        var command = Guid.NewGuid();
        await using var connection = await _connections.OpenConnection(ct);
        await connection.ExecuteAsync(
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
                parent.id,
                payment.id,
                payment."accountId",
                parent."customerId",
                'Refund',
                payment.amount,
                payment."currencyCode",
                'Existing financial operation awaiting final confirmation.',
                parent.version,
                @Correlation,
                'Queued'
            FROM "disputes"."disputes" parent
            JOIN "transactions"."transactions" payment ON payment.id = parent."transactionId"
            WHERE parent."publicId" = @Dispute
            """,
            new
            {
                Command = command,
                Request = Guid.NewGuid(),
                Correlation = Guid.NewGuid(),
                Dispute = parent.Id
            });
        var blocked = await Assert.ThrowsAsync<PortalConflictException>(() => _repository.ReinstateCustomerDispute(
            Customer,
            parent.Id,
            request,
            "pending-reinstate",
            ct));
        Assert.Contains("processing financial operation", blocked.Message);
        Assert.True((await _repository.AssessDispute(Customer, Transaction, ct)).HasPendingFinancialOperation);
        Assert.Equal(
            1,
            await connection.ExecuteScalarAsync<int>("""
            SELECT COUNT(*)
            FROM "disputes"."disputes"
            """));
        Assert.Equal(parent.Version, (await _repository.GetCustomerDispute(Customer, parent.Id, ct))!.Dispute.Version);
        Assert.Equal(
            0,
            await connection.ExecuteScalarAsync<int>("""
            SELECT COUNT(*)
            FROM "disputes"."reinstatements"
            """));
        await connection.ExecuteAsync(
            """
            UPDATE "disputes"."financialCommands"
            SET state = 'Failed',
                "failureCode" = 'IsolatedFixtureFailure',
                "completedAt" = @Now
            WHERE "publicId" = @Command
            """,
            new { Command = command, Now = DateTimeOffset.UtcNow });
        Assert.False((await _repository.AssessDispute(Customer, Transaction, ct)).HasPendingFinancialOperation);
        var child = await _repository.ReinstateCustomerDispute(Customer, parent.Id, request, "pending-reinstate", ct);
        Assert.NotEqual(parent.Id, child.Id);
        Assert.Equal("Submitted", child.Status);
        Assert.Equal(
            child.Id,
            (await _repository.ReinstateCustomerDispute(Customer, parent.Id, request, "pending-reinstate-replay", ct)).Id);
        Assert.Equal("Posted", (await _repository.GetTransaction(Customer, Transaction, ct))!.Status);
        Assert.Equal(Transaction, (await _repository.GetCustomerDispute(Customer, child.Id, ct))!.Dispute.TransactionId);
    }
}
