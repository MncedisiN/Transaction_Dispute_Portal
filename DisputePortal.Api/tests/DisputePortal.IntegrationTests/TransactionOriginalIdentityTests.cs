using Dapper;
using DisputePortal.Api.Models.Internal;
using DisputePortal.Api.Models.Requests;
using DisputePortal.Api.Service.Disputes;
using Xunit;
using DisputePortal.Api.Models.Domain;

namespace DisputePortal.IntegrationTests;

public sealed partial class DisputeWorkflowRepositoryTests
{
    [Fact]
    public async Task Disputing_and_refunding_preserve_one_original_transaction_and_one_real_credit_on_replay()
    {
        await using var connection = await _connections.OpenConnection(default);
        const string originalSql = """
            SELECT "transactions"."publicId" AS "id",
            "transactions"."transactionReference" AS Reference,
            (
                SELECT "related1"."publicId"
                FROM "accounts"."accounts" AS "related1"
                WHERE "related1"."id" = "transactions"."accountId") AS AccountId,
            "transactions"."direction" AS "direction",
            (
                SELECT "related2"."code"
                FROM "configuration"."transactionTypes" AS "related2"
                WHERE "related2"."id" = "transactions"."transactionTypeId") AS Type,
            "transactions"."amount" AS "amount",
            "transactions"."currencyCode" AS "currency"
            FROM "transactions"."transactions" AS "transactions"
            WHERE "transactions"."publicId" = @Id
            """;
        var original = await connection.QuerySingleAsync<(Guid Id, string Reference, Guid AccountId, string Direction, string Type, decimal Amount, string Currency)>(
            originalSql,
            new { Id = Transaction });
        var ids = (await connection.QueryAsync<Guid>("""
            SELECT "transactions"."publicId" AS "id"
            FROM "transactions"."transactions" AS "transactions"
            ORDER BY "transactions"."id"
            """)).ToArray();
        var command = new CreateDisputeCommand
        {
            TransactionId = Transaction,
            Category = "Unrecognized",
            Reason = "I did not make this transaction",
            Description = "Please investigate this payment without duplicating its original transaction."
        };
        var submissionKey = "original-identity-" + Guid.NewGuid().ToString("N");
        var created = await _repository.CreateDispute(Customer, command, submissionKey, default);
        var replayedDispute = await _repository.CreateDispute(Customer, command, submissionKey, default);
        Assert.Equal(created.Id, replayedDispute.Id);
        Assert.True(replayedDispute.Replayed);
        Assert.Equal(
            ids,
            (await connection.QueryAsync<Guid>("""
            SELECT "transactions"."publicId" AS "id"
            FROM "transactions"."transactions" AS "transactions"
            ORDER BY "transactions"."id"
            """)).ToArray());
        var linked = await _repository.GetCustomerDispute(Customer, created.Id, default);
        Assert.Equal(original.Id, linked!.Dispute.TransactionId);
        var version = await _repository.TransitionDispute(
            Agent,
            created.Id,
            new TransitionCommand(linked.Dispute.Version, "UnderReview", "Original payment verified for a policy-compliant refund."),
            "transaction-identity",
            default);
        var request = new ApproveRefundRequest(
            version,
            Guid.NewGuid(),
            "Original payment confirmed; issue exactly one linked refund credit.");
        var refund = await Finance().ApproveRefund(Agent, created.Id, request, "transaction-identity", default);
        var replayedRefund = await Finance().ApproveRefund(Agent, created.Id, request, "transaction-identity", default);
        Assert.Equal(refund.CreditTransactionId, replayedRefund.CreditTransactionId);
        Assert.True(replayedRefund.Replayed);
        Assert.NotEqual(original.Id, refund.CreditTransactionId);
        Assert.Equal(
            original,
            await connection.QuerySingleAsync<(Guid Id, string Reference, Guid AccountId, string Direction, string Type, decimal Amount, string Currency)>(
            originalSql,
            new { Id = Transaction }));
        Assert.Equal(
            ids.Length + 1,
            await connection.ExecuteScalarAsync<int>("""
            SELECT COUNT(*)
            FROM "transactions"."transactions" AS "transactions"
            """));
        Assert.Equal(
            1,
            await connection.ExecuteScalarAsync<int>(
                """
            SELECT COUNT(*)
            FROM "transactions"."transactions" AS "transactions"
            WHERE(
                SELECT "related1"."publicId"
                FROM "accounts"."accounts" AS "related1"
                WHERE "related1"."id" = "transactions"."accountId") = @AccountId
            AND "transactions"."transactionReference" = @Reference
            AND "transactions"."direction" = 'Debit'
            """,
                new { original.AccountId, original.Reference }));
        Assert.Equal(
            1,
            await connection.ExecuteScalarAsync<int>(
                """
            SELECT COUNT(*)
            FROM "disputes"."refunds" AS refund
            JOIN "transactions"."transactions" AS source
            ON "source"."id" = "refund"."transactionId"
            JOIN "transactions"."transactions" AS credit
            ON "credit"."id" = "refund"."creditTransactionId"
            WHERE "source"."publicId" = @Original
            AND "credit"."publicId" = @Credit
            AND "credit"."direction" = 'Credit'
            AND(
                SELECT "related1"."code"
                FROM "configuration"."transactionTypes" AS "related1"
                WHERE "related1"."id" = "credit"."transactionTypeId") = 'Refund'
            """,
                new { Original = original.Id, Credit = refund.CreditTransactionId }));
        var visible = await _repository.GetTransactions(
            Customer,
            new TransactionFilter
            {
                Page = 1,
                PageSize = 100,
                Search = null,
                From = null,
                To = null,
                Direction = null,
                Type = null,
                Sort = "date_desc"
            },
            default);
        Assert.Equal(visible.Items.Count, visible.Items.Select(item => item.Id).Distinct().Count());
        Assert.Equal(created.Id, Assert.Single(visible.Items, item => item.Id == original.Id).DisputeId);
        Assert.Equal(created.Id, Assert.Single(visible.Items, item => item.Id == refund.CreditTransactionId).DisputeId);
    }
}
