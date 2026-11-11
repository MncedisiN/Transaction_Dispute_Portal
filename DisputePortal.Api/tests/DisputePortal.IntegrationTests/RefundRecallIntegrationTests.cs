using Dapper;
using System.Data.Common;
using DisputePortal.Api.Models.Exceptions;
using DisputePortal.Api.Models.Requests;
using Xunit;

namespace DisputePortal.IntegrationTests;

public sealed partial class DisputeWorkflowRepositoryTests
{
    private async Task FundRecallAccount(decimal amount = 250m)
    {
        await using var c = await _connections.OpenConnection(CancellationToken.None);
        var id = Guid.NewGuid();
        await c.ExecuteAsync(
            """
            INSERT INTO "transactions"."transactions"("publicId",
                "accountId",
                "transactionReference",
                "transactionAt",
                "description",
                "merchantName",
                "amount",
                "currencyCode",
                "transactionTypeId",
                "direction",
                "transactionStatus",
                "disputeEligible")
            VALUES(@Id,
                (
                    SELECT "resolved1"."id"
                    FROM "accounts"."accounts" AS "resolved1"
                    WHERE "resolved1"."publicId" = @Account),
                @Reference,
                @Now,
                'Funding for refund recall',
                'Test',
                @Amount,
                'ZAR',
                (
                    SELECT "resolved2"."id"
                    FROM "configuration"."transactionTypes" AS "resolved2"
                    WHERE "resolved2"."code" = 'Deposit'),
                'Credit',
                'Posted',
                @No)
            """,
            new
            {
                Id = id,
                Account,
                Reference = id.ToString(),
                Now = DateTimeOffset.UtcNow,
                Amount = amount,
                No = false
            });
    }

    [Fact]
    public async Task Refund_recall_is_linked_balanced_immutable_and_idempotent_under_concurrency()
    {
        var ct = CancellationToken.None;
        var reviewed = await ReviewableRefundCase();
        var finance = Finance();
        await FundRecallAccount();
        var baseline = (await finance.GetBalances(Customer, ct)).Single(x => x.AccountId == Account);
        var refund = await finance.ApproveRefund(
            Agent,
            reviewed.Id,
            new(reviewed.Version, Guid.NewGuid(), "The original debit qualifies for a full refund."),
            "refund-recall-test",
            ct);
        var closed = (await _repository.GetCustomerDispute(Customer, reviewed.Id, ct))!.Dispute;
        Assert.False((await finance.GetCaseFinancials(Customer, reviewed.Id, ct)).CanRecall);
        Assert.True((await finance.GetCaseFinancials(Agent, reviewed.Id, ct)).CanRecall);
        await using var c = await _connections.OpenConnection(ct);
        var request = new RecallRefundRequest(
            closed.Version,
            Guid.NewGuid(),
            "New merchant evidence invalidates the original refund decision.");
        await Assert.ThrowsAsync<PortalNotFoundException>(() => finance.RecallRefund(
            OtherCustomer,
            reviewed.Id,
            request,
            "unauthorised",
            ct));
        var results = await Task.WhenAll(
            finance.RecallRefund(Agent, reviewed.Id, request, "recall-a", ct),
            finance.RecallRefund(Agent, reviewed.Id, request, "recall-b", ct));
        Assert.Equal(results[0].Id, results[1].Id);
        Assert.Single(results, result => result.Replayed);
        Assert.Equal(refund.CreditTransactionId, results[0].CreditTransactionId);
        Assert.NotEqual(refund.CreditTransactionId, results[0].ReversalTransactionId);
        Assert.Equal(baseline.CurrentBalance, (await finance.GetBalances(Customer, ct)).Single(x => x.AccountId == Account).CurrentBalance);
        var summary = await finance.GetCaseFinancials(Customer, reviewed.Id, ct);
        Assert.True(summary.CanReinstate);
        Assert.Equal(request.Reason, summary.Recall!.Reason);
        Assert.Equal(refund.CreditTransactionId, summary.Refund!.CreditTransactionId);
        Assert.Contains(
            await finance.GetCaseActivity(Customer, reviewed.Id, ct),
            entry => entry.Action == "DisputeRefundRecalled" && entry.Detail == request.Reason);
        await Assert.ThrowsAsync<PortalConflictException>(() => finance.RecallRefund(
            Agent,
            reviewed.Id,
            new RecallRefundRequest(request) { RequestId = Guid.NewGuid() },
            "duplicate",
            ct));
        Assert.Equal(
            0m,
            await c.ExecuteScalarAsync<decimal>(
                """
            SELECT SUM("financial_ledger"."currentDelta")
            FROM "ledger"."projectionEntries" AS "financial_ledger"
            WHERE(
                SELECT "related1"."publicId"
                FROM "transactions"."transactions" AS "related1"
                WHERE "related1"."id" = "financial_ledger"."transactionId") = @Credit
            OR(
                SELECT "related2"."publicId"
                FROM "transactions"."transactions" AS "related2"
                WHERE "related2"."id" = "financial_ledger"."transactionId") = @Reversal
            """,
                new { Credit = refund.CreditTransactionId, Reversal = results[0].ReversalTransactionId }));
        Assert.Equal(
            "Posted",
            await c.ExecuteScalarAsync<string>(
                """
            SELECT "transactions"."transactionStatus" AS "transaction_status"
            FROM "transactions"."transactions" AS "transactions"
            WHERE "transactions"."publicId" = @Id
            """,
                new { Id = refund.CreditTransactionId }));
        Assert.Equal(
            1,
            await c.ExecuteScalarAsync<int>(
                """
            SELECT COUNT(*)
            FROM "notifications"."notifications" AS "notifications"
            WHERE(
                SELECT "related1"."publicId"
                FROM "identity"."users" AS "related1"
                WHERE "related1"."id" = "notifications"."userId") = @Customer
            AND(
                SELECT "related2"."publicId"
                FROM "disputes"."disputes" AS "related2"
                WHERE "related2"."id" = "notifications"."disputeId") = @Id
            AND "notifications"."notificationType" = 'RefundRecalled'
            """,
                new { Customer, Id = reviewed.Id }));
        Assert.Equal(
            1,
            await c.ExecuteScalarAsync<int>(
                """
            SELECT COUNT(*)
            FROM "notifications"."events" e
            JOIN "identity"."users" u
            ON "u"."id" = "e"."userId"
            JOIN "disputes"."disputes" d
            ON "d"."id" = "e"."disputeId"
            WHERE "u"."publicId" = @Customer
            AND "d"."publicId" = @Id
            AND "e"."kind" = 'RefundRecalled'
            """,
                new { Customer, Id = reviewed.Id }));
        await Assert.ThrowsAnyAsync<DbException>(() => c.ExecuteAsync(
            """
            UPDATE "disputes"."refundRecalls"
            SET "reason" = 'Altered historic explanation'
            WHERE "refundRecalls"."publicId" = @Id
            """,
            new { Id = results[0].Id }));
        await Assert.ThrowsAnyAsync<DbException>(() => c.ExecuteAsync(
            """
            UPDATE "transactions"."transactions"
            SET "amount" = 1
            WHERE "transactions"."publicId" = @Id
            """,
            new { Id = results[0].ReversalTransactionId }));
        await Assert.ThrowsAnyAsync<DbException>(() => c.ExecuteAsync(
            """
            DELETE
            FROM "disputes"."refundRecalls"
            WHERE "refundRecalls"."publicId" = @Id
            """,
            new { Id = results[0].Id }));
    }

    [Fact]
    public async Task Refund_recall_respects_reservations_and_rolls_back_without_partial_money_or_notifications()
    {
        var ct = CancellationToken.None;
        var reviewed = await ReviewableRefundCase();
        var finance = Finance();
        await FundRecallAccount(150m);
        await finance.ApproveRefund(
            Agent,
            reviewed.Id,
            new(reviewed.Version, Guid.NewGuid(), "The original debit qualifies for a full refund."),
            "refund-reserved-test",
            ct);
        var current = (await _repository.GetCustomerDispute(Customer, reviewed.Id, ct))!.Dispute;
        var hold = Guid.NewGuid();
        await using var c = await _connections.OpenConnection(ct);
        await c.ExecuteAsync(
            """
            INSERT INTO "transactions"."transactions"("publicId",
                "accountId",
                "transactionReference",
                "transactionAt",
                "description",
                "merchantName",
                "amount",
                "currencyCode",
                "transactionTypeId",
                "direction",
                "transactionStatus",
                "disputeEligible")
            VALUES(@Id,
                (
                    SELECT "resolved1"."id"
                    FROM "accounts"."accounts" AS "resolved1"
                    WHERE "resolved1"."publicId" = @Account),
                @Reference,
                @Now,
                'Pending purchase',
                'Test',
                75,
                'ZAR',
                (
                    SELECT "resolved2"."id"
                    FROM "configuration"."transactionTypes" AS "resolved2"
                    WHERE "resolved2"."code" = 'CardPurchase'),
                'Debit',
                'Pending',
                @No)
            """,
            new
            {
                Id = hold,
                Account,
                Reference = hold.ToString(),
                Now = DateTimeOffset.UtcNow,
                No = false
            });
        var balance = (await finance.GetBalances(Customer, ct)).Single(x => x.AccountId == Account);
        Assert.Equal(150m, balance.CurrentBalance);
        Assert.Equal(75m, balance.AvailableBalance);
        var journalCount = await c.ExecuteScalarAsync<int>("""
            SELECT COUNT(*)
            FROM "ledger"."projectionEntries" AS "financial_ledger"
            """);
        var request = new RecallRefundRequest(
            current.Version,
            Guid.NewGuid(),
            "New evidence requires recalling the previously approved refund.");
        await Assert.ThrowsAsync<PortalConflictException>(() => finance.RecallRefund(Agent, reviewed.Id, request, "insufficient", ct));
        Assert.Equal(
            0,
            await c.ExecuteScalarAsync<int>("""
            SELECT COUNT(*)
            FROM "disputes"."refundRecalls" AS "dispute_refund_recalls"
            """));
        Assert.Equal(
            0,
            await c.ExecuteScalarAsync<int>("""
            SELECT COUNT(*)
            FROM "transactions"."transactions" AS "transactions"
            WHERE(
                SELECT "related1"."code"
                FROM "configuration"."transactionTypes" AS "related1"
                WHERE "related1"."id" = "transactions"."transactionTypeId") = 'RefundReversal'
            """));
        Assert.Equal(
            journalCount,
            await c.ExecuteScalarAsync<int>("""
            SELECT COUNT(*)
            FROM "ledger"."projectionEntries" AS "financial_ledger"
            """));
        Assert.Equal(current.Version, (await _repository.GetCustomerDispute(Customer, reviewed.Id, ct))!.Dispute.Version);
        Assert.Equal(
            0,
            await c.ExecuteScalarAsync<int>("""
            SELECT COUNT(*)
            FROM "notifications"."notifications" AS "notifications"
            WHERE "notifications"."notificationType" = 'RefundRecalled'
            """));
        await c.ExecuteAsync(
            """
            UPDATE "transactions"."transactions"
            SET "transactionStatus" = 'Reversed'
            WHERE "transactions"."publicId" = @Id
            """,
            new { Id = hold });
        Assert.Equal(100m, (await finance.RecallRefund(Agent, reviewed.Id, request, "funds-released", ct)).Amount);
    }

    [Fact]
    public async Task Reinstatement_creates_one_linked_case_and_allows_a_new_qualified_refund()
    {
        var ct = CancellationToken.None;
        var reviewed = await ReviewableRefundCase();
        var finance = Finance();
        await FundRecallAccount();
        await finance.ApproveRefund(
            Agent,
            reviewed.Id,
            new(reviewed.Version, Guid.NewGuid(), "Original payment reviewed and refund authorised."),
            "initial",
            ct);
        var closed = (await _repository.GetCustomerDispute(Customer, reviewed.Id, ct))!.Dispute;
        var request = new ReinstateDisputeRequest(
            closed.Version,
            Guid.NewGuid(),
            "I have new evidence that supports my dispute after the recall.");
        await Assert.ThrowsAsync<PortalConflictException>(() => _repository.ReinstateCustomerDispute(
            Customer,
            reviewed.Id,
            request,
            "before-recall",
            ct));
        await finance.RecallRefund(
            Agent,
            reviewed.Id,
            new(closed.Version, Guid.NewGuid(), "Merchant evidence requires the original refund to be recalled."),
            "recall",
            ct);
        await Assert.ThrowsAsync<PortalConflictException>(() => _repository.ReinstateCustomerDispute(
            Customer,
            reviewed.Id,
            request,
            "stale",
            ct));
        request = new ReinstateDisputeRequest(request)
        {
            ExpectedVersion = (await _repository.GetCustomerDispute(Customer, reviewed.Id, ct))!.Dispute.Version
        };
        await Assert.ThrowsAsync<PortalNotFoundException>(() => _repository.ReinstateCustomerDispute(
            OtherCustomer,
            reviewed.Id,
            request,
            "wrong-owner",
            ct));
        var results = await Task.WhenAll(
            _repository.ReinstateCustomerDispute(Customer, reviewed.Id, request, "reinstate-a", ct),
            _repository.ReinstateCustomerDispute(Customer, reviewed.Id, request, "reinstate-b", ct));
        Assert.Equal(results[0].Id, results[1].Id);
        Assert.NotEqual(reviewed.Id, results[0].Id);
        Assert.Single(results, result => result.Replayed);
        Assert.False((await finance.GetCaseFinancials(Customer, reviewed.Id, ct)).CanReinstate);
        Assert.Equal(results[0].Id, (await finance.GetCaseFinancials(Customer, reviewed.Id, ct)).ReinstatedDisputeId);
        Assert.Equal(reviewed.Id, (await finance.GetCaseFinancials(Customer, results[0].Id, ct)).ParentDisputeId);
        await Assert.ThrowsAsync<PortalConflictException>(() => _repository.ReinstateCustomerDispute(
            Customer,
            reviewed.Id,
            new ReinstateDisputeRequest(request) { RequestId = Guid.NewGuid() },
            "duplicate",
            ct));
        var child = (await _repository.GetCustomerDispute(Customer, results[0].Id, ct))!.Dispute;
        Assert.Equal(closed.TransactionId, child.TransactionId);
        var reviewedVersion = await _repository.TransitionDispute(
            Agent,
            child.Id,
            new(child.Version, "UnderReview", "New evidence is being reviewed."),
            "child-review",
            ct);
        var newRefund = await finance.ApproveRefund(
            Agent,
            child.Id,
            new(reviewedVersion, Guid.NewGuid(), "New evidence now supports the full eligible refund."),
            "child-refund",
            ct);
        Assert.Equal(100m, newRefund.Amount);
        await using var c = await _connections.OpenConnection(ct);
        Assert.Equal(
            2,
            await c.ExecuteScalarAsync<int>(
                """
            SELECT COUNT(*)
            FROM "disputes"."refunds" AS "dispute_refunds"
            WHERE(
                SELECT "related1"."publicId"
                FROM "transactions"."transactions" AS "related1"
                WHERE "related1"."id" = "dispute_refunds"."transactionId") = @Transaction
            """,
                new { Transaction }));
        Assert.Equal(
            1,
            await c.ExecuteScalarAsync<int>(
                """
            SELECT COUNT(*)
            FROM "disputes"."refunds" r
            WHERE(
                SELECT "related1"."publicId"
                FROM "transactions"."transactions" AS "related1"
                WHERE "related1"."id" = "r"."transactionId") = @Transaction
            AND NOT EXISTS(
                SELECT 1
                FROM "disputes"."refundRecalls" recall
                WHERE "recall"."disputeId" = "r"."disputeId")
            """,
                new { Transaction }));
        Assert.Equal("Resolved", (await _repository.GetCustomerDispute(Customer, reviewed.Id, ct))!.Dispute.Status);
    }

    [Fact]
    public async Task Manual_unassignment_stays_unassigned_until_a_qualified_staff_member_claims_it()
    {
        var ct = CancellationToken.None;
        var reviewed = await ReviewableRefundCase();
        await using var c = await _connections.OpenConnection(ct);
        await c.ExecuteAsync(
            """
            INSERT INTO "identity"."userRoles"("userId",
                "roleId")
            SELECT(
                SELECT "resolved1"."id"
                FROM "identity"."users" AS "resolved1"
                WHERE "resolved1"."publicId" = @Id),
            "roles"."id"
            FROM "identity"."roles" AS "roles"
            WHERE "roles"."code" = 'operations_manager'
            """,
            new { Id = Agent });
        await _repository.Assign(Agent, reviewed.Id, new(reviewed.Version, null, "Release this case for a new specialist."), ct);
        var released = await _repository.GetCase(Agent, reviewed.Id, ct);
        Assert.Null(released.AssignedTo);
        Assert.Null(released.AssignedAt);
        Assert.True(released.AutoAssignmentPaused);
        await _repository.AssignWaitingCases(ct);
        Assert.Null((await _repository.GetCase(Agent, reviewed.Id, ct)).AssignedTo);
        await Assert.ThrowsAsync<PortalConflictException>(() => _repository.Assign(
            Agent,
            reviewed.Id,
            new(reviewed.Version, null, "Stale unassignment should be rejected."),
            ct));
        await _repository.ClaimCase(Agent, reviewed.Id, new(released.Version, "I can take over this released case."), ct);
        var claimed = await _repository.GetCase(Agent, reviewed.Id, ct);
        Assert.Equal(Agent, claimed.AssignedTo);
        Assert.False(claimed.AutoAssignmentPaused);
        Assert.Equal(
            1,
            await c.ExecuteScalarAsync<int>(
                """
            SELECT COUNT(*)
            FROM "notifications"."notifications" AS "notifications"
            WHERE(
                SELECT "related1"."publicId"
                FROM "identity"."users" AS "related1"
                WHERE "related1"."id" = "notifications"."userId") = @Customer
            AND(
                SELECT "related2"."publicId"
                FROM "disputes"."disputes" AS "related2"
                WHERE "related2"."id" = "notifications"."disputeId") = @Id
            AND "notifications"."notificationType" = 'CaseUnassigned'
            """,
                new { Customer, Id = reviewed.Id }));
    }

    [Theory]
    [InlineData("Resolved")]
    [InlineData("Declined")]
    [InlineData("Withdrawn")]
    public async Task Only_super_admin_can_reopen_eligible_terminal_cases(string terminal)
    {
        var ct = CancellationToken.None;
        var reviewed = await ReviewableRefundCase();
        var version = terminal == "Withdrawn" ? await _repository.WithdrawCustomerDispute(
            Customer,
            reviewed.Id,
            reviewed.Version,
            "withdraw-for-reopen",
            ct) : await _repository.TransitionDispute(
            Agent,
            reviewed.Id,
            new(reviewed.Version, terminal, "Review completed without a financial refund."),
            "close-for-reopen",
            ct);
        await using var c = await _connections.OpenConnection(ct);
        await c.ExecuteAsync(
            """
            INSERT INTO "identity"."userRoles"("userId",
                "roleId")
            SELECT(
                SELECT "resolved1"."id"
                FROM "identity"."users" AS "resolved1"
                WHERE "resolved1"."publicId" = @Id),
            "roles"."id"
            FROM "identity"."roles" AS "roles"
            WHERE "roles"."code" = 'operations_manager'
            """,
            new { Id = Agent });
        await Assert.ThrowsAsync<PortalForbiddenException>(() => _repository.TransitionDispute(
            Agent,
            reviewed.Id,
            new(version, "UnderReview", "New evidence requires reopening this closed case."),
            "manager-reopen",
            ct));
        await c.ExecuteAsync(
            """
            INSERT INTO "identity"."userRoles"("userId",
                "roleId")
            SELECT(
                SELECT "resolved1"."id"
                FROM "identity"."users" AS "resolved1"
                WHERE "resolved1"."publicId" = @Id),
            "roles"."id"
            FROM "identity"."roles" AS "roles"
            WHERE "roles"."code" = 'super_admin'
            """,
            new { Id = Agent });
        await _repository.TransitionDispute(
            Agent,
            reviewed.Id,
            new(version, "UnderReview", "New evidence requires reopening this closed case."),
            "super-reopen",
            ct);
        var reopened = (await _repository.GetCustomerDispute(Customer, reviewed.Id, ct))!.Dispute;
        Assert.Equal("UnderReview", reopened.Status);
        Assert.Null(reopened.Resolution);
        Assert.Equal(
            2,
            await c.ExecuteScalarAsync<int>(
                """
            SELECT "transaction_disputes"."workflowCycle" AS "workflow_cycle"
            FROM "disputes"."disputes" AS "transaction_disputes"
            WHERE "transaction_disputes"."publicId" = @Id
            """,
                new { Id = reviewed.Id }));
        Assert.Equal(
            1,
            await c.ExecuteScalarAsync<int>(
                """
            SELECT COUNT(*)
            FROM "notifications"."notifications" AS "notifications"
            WHERE(
                SELECT "related1"."publicId"
                FROM "identity"."users" AS "related1"
                WHERE "related1"."id" = "notifications"."userId") = @Customer
            AND(
                SELECT "related2"."publicId"
                FROM "disputes"."disputes" AS "related2"
                WHERE "related2"."id" = "notifications"."disputeId") = @Id
            AND "notifications"."notificationType" = 'DisputeReopened'
            """,
                new { Customer, Id = reviewed.Id }));
    }
}
