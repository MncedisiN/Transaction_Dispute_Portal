using Dapper;
using System.Data.Common;
using Xunit;

namespace DisputePortal.IntegrationTests;

public sealed partial class DisputeWorkflowRepositoryTests
{
    [Fact]
    public async Task Refund_recall_database_rejection_rolls_back_the_already_inserted_debit_and_journal()
    {
        var ct = CancellationToken.None;
        var reviewed = await ReviewableRefundCase();
        var finance = Finance();
        await FundRecallAccount();
        await finance.ApproveRefund(
            Agent,
            reviewed.Id,
            new(reviewed.Version, Guid.NewGuid(), "Original payment reviewed and refund authorised."),
            "atomicity-refund",
            ct);
        var current = (await _repository.GetCustomerDispute(Customer, reviewed.Id, ct))!.Dispute;
        var balance = (await finance.GetBalances(Customer, ct)).Single(x => x.AccountId == Account);
        var request = new DisputePortal.Api.Models.Requests.RecallRefundRequest(
            current.Version,
            Guid.NewGuid(),
            "Merchant evidence requires recalling the previously approved refund.");
        await using var c = await _connections.OpenConnection(ct);
        var journalCount = await c.ExecuteScalarAsync<int>("""
            SELECT COUNT(*)
            FROM "ledger"."projectionEntries" AS "financial_ledger"
            """);
        // Force failure after the debit transaction's journal trigger has run.
        await c.ExecuteAsync("ALTER TABLE \"disputes\".\"refundRecalls\" ADD CONSTRAINT ck_test_block_recall CHECK(amount<0)");
        try
        {
            await Assert.ThrowsAnyAsync<DbException>(() => finance.RecallRefund(Agent, reviewed.Id, request, "forced-rejection", ct));
            Assert.Equal(
                journalCount,
                await c.ExecuteScalarAsync<int>("""
                SELECT COUNT(*)
                FROM "ledger"."projectionEntries" AS "financial_ledger"
                """));
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
                0,
                await c.ExecuteScalarAsync<int>("""
                SELECT COUNT(*)
                FROM "notifications"."notifications" AS "notifications"
                WHERE "notifications"."notificationType" = 'RefundRecalled'
                """));
            Assert.Equal(current.Version, (await _repository.GetCustomerDispute(Customer, reviewed.Id, ct))!.Dispute.Version);
            Assert.Equal(
                balance.CurrentBalance,
                (await finance.GetBalances(Customer, ct)).Single(x => x.AccountId == Account).CurrentBalance);
        }
        finally
        {
            await c.ExecuteAsync("ALTER TABLE \"disputes\".\"refundRecalls\" DROP CONSTRAINT ck_test_block_recall");
        }

        Assert.False((await finance.RecallRefund(Agent, reviewed.Id, request, "retry-after-rejection", ct)).Replayed);
    }
}
