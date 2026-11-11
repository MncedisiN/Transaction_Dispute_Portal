using DisputePortal.Api.Models.Domain;
using Dapper;
using DisputePortal.Api.Service.Disputes;
using Xunit;

namespace DisputePortal.IntegrationTests;

public sealed partial class DisputeWorkflowRepositoryTests
{
    [Fact]
    public async Task Customer_transaction_and_dispute_presentation_retains_original_bank_records()
    {
        await using var c = await _connections.OpenConnection(default);
        const string original = "Repeated card payment below refund threshold";
        await c.ExecuteAsync(
            """
            UPDATE "transactions"."transactions"
            SET "description" = @Description,
            "merchantName" = 'City Fuel'
            WHERE "transactions"."publicId" = @Transaction
            """,
            new { Description = original, Transaction });
        var transaction = (await _repository.GetTransaction(Customer, Transaction, default))!;
        Assert.Equal("Card Purchase", transaction.TypeLabel);
        Assert.Equal("Card Purchase · City Fuel", transaction.Description);
        Assert.Null(transaction.Reference);
        var created = await _repository.CreateDispute(
            Customer,
            new CreateDisputeCommand
            {
                TransactionId = Transaction,
                Category = "Unrecognized",
                Reason = "Not recognised",
                Description = "Please investigate this transaction."
            },
            "presentation-test",
            default);
        var detail = (await _repository.GetCustomerDispute(Customer, created.Id, default))!;
        Assert.Equal("CardPurchase", detail.Dispute.TransactionType);
        Assert.Equal("Card Purchase", detail.Dispute.TransactionTypeLabel);
        Assert.Equal("Card Purchase · City Fuel", detail.Dispute.TransactionDescription);
        Assert.Equal(
            original,
            await c.ExecuteScalarAsync<string>(
                """
            SELECT "transactions"."description" AS "description"
            FROM "transactions"."transactions" AS "transactions"
            WHERE "transactions"."publicId" = @Transaction
            """,
                new { Transaction }));
    }
}
