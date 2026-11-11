using DisputePortal.Api.Models.Domain;
using Dapper;
using DisputePortal.Api.Models.Requests;
using DisputePortal.Api.Repository;
using DisputePortal.Api.Repository.Data;
using DisputePortal.Api.Service.Disputes;
using Xunit;

namespace DisputePortal.IntegrationTests;

public sealed partial class DisputeWorkflowRepositoryTests
{
    [Fact]
    public async Task Staff_refund_receipt_projection_and_search_use_the_refund_identity_instead_of_the_dispute_identity()
    {
        var ct = CancellationToken.None;
        var unrelated = await _repository.CreateDispute(
            OtherCustomer,
            new CreateDisputeCommand
            {
                TransactionId = OtherTransaction,
                Category = "Unrecognized",
                Reason = "I did not make this transaction",
                Description = "Please investigate this unrelated merchant payment."
            },
            "receipt-unrelated-case",
            ct);
        var unrelatedVersion = (await _repository.GetCustomerDispute(OtherCustomer, unrelated.Id, ct))!.Dispute.Version;
        await _repository.WithdrawCustomerDispute(OtherCustomer, unrelated.Id, unrelatedVersion, "receipt-unrelated-withdrawal", ct);
        var reviewed = await ReviewableRefundCase();
        var credit = await Finance().ApproveRefund(
            Agent,
            reviewed.Id,
            new ApproveRefundRequest(reviewed.Version, Guid.NewGuid(), "Merchant evidence confirms this purchase must be refunded."),
            "staff-refund-receipt",
            ct);
        await using var connection = await _connections.OpenConnection(ct);
        var persisted = await connection.QuerySingleAsync<(long RefundId, long DisputeId, string CustomerReference)>(new CommandDefinition(
            """
            SELECT refund."id" AS RefundId,
                refund."disputeId" AS DisputeId,
                receipt."customerReference" AS CustomerReference
            FROM "disputes"."refunds" AS refund
            JOIN "disputes"."disputes" AS dispute
                ON dispute."id" = refund."disputeId"
            JOIN "disputes"."refundReceipts" AS receipt
                ON receipt."refundId" = refund."id"
            WHERE dispute."publicId" = @DisputeId
            """,
            new { DisputeId = reviewed.Id },
            cancellationToken: ct));
        Assert.NotEqual(persisted.DisputeId, persisted.RefundId);
        Assert.Equal(credit.CustomerReference, persisted.CustomerReference);
        Assert.Matches("^RF-[0-9]{8}$", persisted.CustomerReference);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO "identity"."userRoles" ("userId", "roleId")
            SELECT userRecord."id",
                roleRecord."id"
            FROM "identity"."users" AS userRecord
            CROSS JOIN "identity"."roles" AS roleRecord
            WHERE userRecord."publicId" = @UserId
                AND roleRecord."code" = 'super_admin'
            """,
            new { UserId = OtherCustomer },
            cancellationToken: ct));
        var staff = new StaffTransactionRepository(
            _connections,
            DatabaseDialect.GetDatabaseDialect(_provider),
            TimeZoneInfo.FindSystemTimeZoneById("Africa/Johannesburg"),
            new TestProtector());
        var original = (await staff.GetStaffTransactionDetails(OtherCustomer, Transaction, ct)).Transaction;
        Assert.Equal(Transaction, original.Id);
        Assert.Equal(reviewed.Id, original.DisputeId);
        Assert.Null(original.RefundReference);
        var originalSearch = await staff.Search(OtherCustomer, 1, 20, original.Reference, null, null, null, ct);
        var originalMatch = Assert.Single(originalSearch.Items);
        Assert.Equal(Transaction, originalMatch.Id);
        Assert.Equal(reviewed.Id, originalMatch.DisputeId);
        var refund = (await staff.GetStaffTransactionDetails(OtherCustomer, credit.CreditTransactionId, ct)).Transaction;
        Assert.Equal(credit.CreditTransactionId, refund.Id);
        Assert.Equal("Refund", refund.Type);
        Assert.Equal(reviewed.Id, refund.DisputeId);
        Assert.Equal(credit.CreditReference, refund.Reference);
        Assert.Equal(persisted.CustomerReference, refund.RefundReference);
        var receiptSearch = await staff.Search(OtherCustomer, 1, 20, persisted.CustomerReference, null, null, null, ct);
        Assert.Equal(1, receiptSearch.Total);
        var receiptMatch = Assert.Single(receiptSearch.Items);
        Assert.Equal(credit.CreditTransactionId, receiptMatch.Id);
        Assert.Equal(reviewed.Id, receiptMatch.DisputeId);
        Assert.Equal(persisted.CustomerReference, receiptMatch.RefundReference);
        Assert.Null((await Finance().GetCaseFinancials(OtherCustomer, unrelated.Id, ct)).Refund);
    }
}
