using Dapper;
using DisputePortal.Api.Models.Domain;
using DisputePortal.Api.Service.Disputes;
using Xunit;

namespace DisputePortal.IntegrationTests;

public sealed partial class DisputeWorkflowRepositoryTests
{
    [Theory]
    [InlineData("DebitOrder")]
    [InlineData("CreditCardPayment")]
    public async Task Eligible_debit_order_and_confirmed_credit_card_purchases_are_refunded_once_as_system(string type)
    {
        var ct = CancellationToken.None;
        await VerifyProvisionedContact(Customer, "customer@test.local");
        var account = await _repository.OpenAccount(Customer, Guid.NewGuid(), "Savings", 10, ct);
        await _repository.ProcessNextTransactionBatch(ct);
        await using var c = await _connections.OpenConnection(ct);
        Guid transaction;
        if (type == "DebitOrder")
        {
            transaction = Guid.NewGuid();
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
                    "disputeEligible",
                    "debitOrderScheme",
                    "mandateStatus")
                VALUES(@Transaction,
                    (
                        SELECT "resolved1"."id"
                        FROM "accounts"."accounts" AS "resolved1"
                        WHERE "resolved1"."publicId" = @Account),
                    @Reference,
                    @Now,
                    'Debit order collection',
                    'Verified merchant',
                    39,
                    'ZAR',
                    (
                        SELECT "resolved2"."id"
                        FROM "configuration"."transactionTypes" AS "resolved2"
                        WHERE "resolved2"."code" = 'DebitOrder'),
                    'Debit',
                    'Posted',
                    @Eligible,
                    'EFT',
                    'Unknown')
                """,
                new
                {
                    Transaction = transaction,
                    Account = account.AccountId,
                    Reference = Guid.NewGuid().ToString(),
                    Now = DateTimeOffset.UtcNow.AddMinutes(-1),
                    Eligible = true
                });
        }
        else
        {
            transaction = Guid.NewGuid();
            var original = Guid.NewGuid();
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
                VALUES(@Original,
                    (
                        SELECT "resolved1"."id"
                        FROM "accounts"."accounts" AS "resolved1"
                        WHERE "resolved1"."publicId" = @Account),
                    @OriginalRef,
                    @OriginalAt,
                    'Credit card purchase',
                    'Verified merchant',
                    29,
                    'ZAR',
                    (
                        SELECT "resolved2"."id"
                        FROM "configuration"."transactionTypes" AS "resolved2"
                        WHERE "resolved2"."code" = 'CreditCardPayment'),
                    'Debit',
                    'Posted',
                    @Eligible),
                (@Transaction,
                    (
                        SELECT "resolved3"."id"
                        FROM "accounts"."accounts" AS "resolved3"
                        WHERE "resolved3"."publicId" = @Account),
                    @Reference,
                    @Now,
                    'Credit card purchase',
                    'Verified merchant',
                    29,
                    'ZAR',
                    (
                        SELECT "resolved4"."id"
                        FROM "configuration"."transactionTypes" AS "resolved4"
                        WHERE "resolved4"."code" = 'CreditCardPayment'),
                    'Debit',
                    'Posted',
                    @Eligible);
                INSERT INTO "transactions"."duplicateConfirmations"("transactionId",
                    "originalTransactionId",
                    "confirmationSource")
                VALUES((
                        SELECT "resolved5"."id"
                        FROM "transactions"."transactions" AS "resolved5"
                        WHERE "resolved5"."publicId" = @Transaction),
                    (
                        SELECT "resolved6"."id"
                        FROM "transactions"."transactions" AS "resolved6"
                        WHERE "resolved6"."publicId" = @Original),
                    'LocalBankFeed')
                """,
                new
                {
                    Original = original,
                    Transaction = transaction,
                    Account = account.AccountId,
                    OriginalRef = Guid.NewGuid().ToString(),
                    Reference = Guid.NewGuid().ToString(),
                    OriginalAt = DateTimeOffset.UtcNow.AddMinutes(-2),
                    Now = DateTimeOffset.UtcNow.AddMinutes(-1),
                    Eligible = true
                });
        }

        Assert.NotEqual(Guid.Empty, transaction);
        var result = await _repository.CreateDispute(
            Customer,
            new CreateDisputeCommand
            {
                TransactionId = transaction,
                Category = type == "DebitOrder" ? "DebitOrder" : "Duplicate",
                Reason = type == "DebitOrder" ? "I cancelled this debit order" : "I was charged more than once",
                Description = "Please review and refund the eligible payment."
            },
            "purchase-auto-" + Guid.NewGuid(),
            ct);
        var finance = Finance();
        var before = (await finance.GetBalances(Customer, ct)).Single(x => x.AccountId == account.AccountId);
        var processed = await Task.WhenAll(finance.ProcessAutomatic(ct), finance.ProcessAutomatic(ct));
        Assert.Single(processed, value => value);
        Assert.False(await finance.ProcessAutomatic(ct));
        var summary = await finance.GetCaseFinancials(Customer, result.Id, ct);
        Assert.NotNull(summary.Refund);
        Assert.True(summary.Refund.Automatic);
        var pinned = await c.QuerySingleAsync<long>(new CommandDefinition(
            """
            SELECT d."refundPolicyVersionId" FROM disputes."automaticDecisions" d
            JOIN disputes.disputes c ON c.id=d."disputeId" WHERE c."publicId"=@Case
            """,
            new { Case = result.Id },
            cancellationToken: ct));
        Assert.Equal(
            pinned,
            await c.QuerySingleAsync<long>(new CommandDefinition(
                """
            SELECT r."refundPolicyVersionId" FROM disputes.refunds r
            JOIN disputes.disputes c ON c.id=r."disputeId" WHERE c."publicId"=@Case
            """,
                new { Case = result.Id },
                cancellationToken: ct)));
        Assert.Equal(type == "DebitOrder" ? AutomaticRefundPolicy.DebitOrderRuleCode : AutomaticRefundPolicy.RuleCode, summary.RuleCode);
        var after = (await finance.GetBalances(Customer, ct)).Single(x => x.AccountId == account.AccountId);
        Assert.Equal(before.CurrentBalance + summary.Refund.Amount, after.CurrentBalance);
        Assert.Equal(
            1,
            await c.ExecuteScalarAsync<int>(
                """
            SELECT COUNT(*)
            FROM "disputes"."refunds" AS "dispute_refunds"
            WHERE(
                SELECT "related1"."publicId"
                FROM "disputes"."disputes" AS "related1"
                WHERE "related1"."id" = "dispute_refunds"."disputeId") = @Id
            AND "dispute_refunds"."approvalSource" = 'System'
            AND(
                SELECT "related2"."publicId"
                FROM "identity"."users" AS "related2"
                WHERE "related2"."id" = "dispute_refunds"."approvedById") IS NULL
            """,
                new { result.Id }));
        var detail = await _repository.GetCustomerDispute(Customer, result.Id, ct);
        Assert.Equal("Resolved", detail!.Dispute.Status);
        Assert.Contains(detail.History, item => item.NewStatus == "UnderReview" && item.ActorName == "System");
        Assert.Contains(detail.History, item => item.NewStatus == "Resolved" && item.ActorName == "System");
    }
}
