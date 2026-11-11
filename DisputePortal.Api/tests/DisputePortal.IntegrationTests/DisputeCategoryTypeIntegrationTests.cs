using DisputePortal.Api.Models.Domain;
using Dapper;
using DisputePortal.Api.Models.Exceptions;
using DisputePortal.Api.Repository;
using DisputePortal.Api.Service.Disputes;
using Xunit;

namespace DisputePortal.IntegrationTests;

public sealed partial class DisputeWorkflowRepositoryTests
{
    [Fact]
    public async Task Category_type_links_are_explicit_unique_nonnullable_foreign_keys()
    {
        await using var connection = await _connections.OpenConnection(default);
        var policy = await new DisputeInputPolicyStore(_connections).GetDisputeInputPolicy(default);
        var registered = (await connection.QueryAsync<string>("SELECT code FROM configuration.\"transactionTypes\"")).Order().ToArray();
        Assert.Equal(registered, policy.Categories.Single(c => c.Code == "Duplicate").TransactionTypes.Order());
        Assert.Equal(new[] { "DebitOrder" }, policy.Categories.Single(c => c.Code == "DebitOrder").TransactionTypes);
        Assert.All(policy.Categories, category => Assert.NotEmpty(category.TransactionTypes));
        const string insert = """
            INSERT INTO configuration."disputeCategoryTransactionTypes" ("categoryId", "transactionTypeId")
            VALUES (@Category, @Type)
            """;
        var pair = await connection.QueryFirstAsync<(long Category, long Type)>("""
            SELECT "categoryId", "transactionTypeId"
            FROM configuration."disputeCategoryTransactionTypes"
            ORDER BY "categoryId", "transactionTypeId"
            """);
        await Assert.ThrowsAnyAsync<System.Data.Common.DbException>(() => connection.ExecuteAsync(insert, new { pair.Category, pair.Type }));
        await Assert.ThrowsAnyAsync<System.Data.Common.DbException>(() => connection.ExecuteAsync(
            insert,
            new { Category = pair.Category, Type = long.MaxValue }));
        await Assert.ThrowsAnyAsync<System.Data.Common.DbException>(() => connection.ExecuteAsync(
            insert,
            new { Category = long.MaxValue, Type = pair.Type }));
        await Assert.ThrowsAnyAsync<System.Data.Common.DbException>(() => connection.ExecuteAsync(
            insert,
            new { Category = pair.Category, Type = (long?)null }));
    }

    [Fact]
    public async Task Repository_rejects_wrong_type_even_when_the_service_and_form_are_bypassed()
    {
        var invalid = new CreateDisputeCommand
        {
            TransactionId = Transaction,
            Category = "DebitOrder",
            Reason = "I cancelled this debit order",
            Description = "Please investigate this transaction in detail."
        };
        var error = await Assert.ThrowsAsync<PortalValidationException>(() => _repository.CreateDispute(
            Customer,
            invalid,
            "wrong-type-direct-001",
            default));
        Assert.Equal("category", error.Field);
        await using var connection = await _connections.OpenConnection(default);
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM disputes.disputes"));
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM disputes.\"statusHistory\""));
    }

    [Fact]
    public async Task Stale_catalogue_cannot_bypass_a_removed_type_mapping()
    {
        var store = new DisputeInputPolicyStore(_connections);
        Assert.Contains(
            "CardPurchase",
            (await store.GetDisputeInputPolicy(default)).Categories.Single(c => c.Code == "Duplicate").TransactionTypes);
        await using var connection = await _connections.OpenConnection(default);
        await connection.ExecuteAsync("""
            DELETE FROM configuration."disputeCategoryTransactionTypes"
            WHERE "categoryId" = (SELECT id FROM configuration."disputeCategories" WHERE code = 'Duplicate')
                AND "transactionTypeId" = (SELECT id FROM configuration."transactionTypes" WHERE code = 'CardPurchase')
            """);
        var request = new CreateDisputeCommand
        {
            TransactionId = Transaction,
            Category = "Duplicate",
            Reason = "I was charged more than once",
            Description = "Please investigate this duplicated transaction."
        };
        var error = await Assert.ThrowsAsync<PortalValidationException>(() => new DisputeService(_repository, store).CreateCustomerDispute(
            Customer,
            request,
            "stale-category-type-001",
            default));
        Assert.Equal("category", error.Field);
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM disputes.disputes"));
        var updated = await new DisputeInputPolicyStore(_connections).GetDisputeInputPolicy(default);
        Assert.DoesNotContain("CardPurchase", updated.Categories.Single(c => c.Code == "Duplicate").TransactionTypes);
        Assert.Contains("CreditCardPayment", updated.Categories.Single(c => c.Code == "Duplicate").TransactionTypes);
    }

    [Fact]
    public async Task Unmapped_transaction_types_receive_no_implicit_category_grants()
    {
        await using var connection = await _connections.OpenConnection(default);
        await connection.ExecuteAsync("""
            DELETE FROM configuration."disputeCategoryTransactionTypes"
            WHERE "transactionTypeId" = (SELECT id FROM configuration."transactionTypes" WHERE code = 'CreditCardPayment')
            """);
        var policy = await new DisputeInputPolicyStore(_connections).GetDisputeInputPolicy(default);
        Assert.All(policy.Categories, category => Assert.DoesNotContain("CreditCardPayment", category.TransactionTypes));
        await connection.ExecuteAsync("""
            INSERT INTO configuration."disputeCategoryTransactionTypes" ("categoryId", "transactionTypeId")
            SELECT category.id, transaction_type.id
            FROM configuration."disputeCategories" category
            CROSS JOIN configuration."transactionTypes" transaction_type
            WHERE category.code = 'Other'
                AND transaction_type.code = 'CreditCardPayment'
            """);
        var updated = await new DisputeInputPolicyStore(_connections).GetDisputeInputPolicy(default);
        Assert.Contains("CreditCardPayment", updated.Categories.Single(c => c.Code == "Other").TransactionTypes);
        Assert.DoesNotContain("CreditCardPayment", updated.Categories.Single(c => c.Code == "Duplicate").TransactionTypes);
    }
}
