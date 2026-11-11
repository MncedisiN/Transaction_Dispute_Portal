using DisputePortal.Api.Models.Domain;
using Dapper;
using DisputePortal.Api.Repository;
using DisputePortal.Api.Models.Exceptions;
using DisputePortal.Api.Service.Disputes;
using Xunit;

namespace DisputePortal.IntegrationTests;

public sealed partial class DisputeWorkflowRepositoryTests
{
    [Fact]
    public async Task Catalogue_and_limits_are_read_from_database_and_changes_need_no_code_defaults()
    {
        await using var connection = await _connections.OpenConnection(default);
        var before = await new DisputeInputPolicyStore(_connections).GetDisputeInputPolicy(default);
        Assert.Equal(6, before.Categories.Count);
        Assert.Equal(10, before.DescriptionMinimumLength);
        await connection.ExecuteAsync(
            """
            UPDATE "configuration"."disputeCategories"
            SET "label" = @Label,
            "reason" = @Reason
            WHERE "disputeCategories"."code" = 'Unrecognized';
            UPDATE configuration."disputeInputPolicies"
            SET "descriptionMinimumLength" = 40,
            "descriptionMaximumLength" = 400;
            """,
            new { Label = "Please Review This Purchase", Reason = "This is the updated customer reason" });
        var updated = await new DisputeInputPolicyStore(_connections).GetDisputeInputPolicy(default);
        Assert.Equal("Please Review This Purchase", updated.Categories.Single(c => c.Code == "Unrecognized").Label);
        Assert.Equal(40, updated.DescriptionMinimumLength);
        Assert.Equal(400, updated.DescriptionMaximumLength);
        var dispatcher = new DisputeService(_repository, new DisputeInputPolicyStore(_connections));
        var invalid = new CreateDisputeCommand
        {
            TransactionId = Transaction,
            Category = "Unrecognized",
            Reason = "I did not make this transaction",
            Description = "Please investigate this transaction in detail."
        };
        var stale = await Assert.ThrowsAsync<PortalValidationException>(() => dispatcher.CreateCustomerDispute(
            Customer,
            invalid,
            "catalogue-change-001",
            default));
        Assert.Equal("reason", stale.Field);
        var shortDescription = new global::DisputePortal.Api.Models.Domain.CreateDisputeCommand(invalid)
        {
            Reason = "This is the updated customer reason",
            Description = "Too short for policy."
        };
        var length = await Assert.ThrowsAsync<PortalValidationException>(() => dispatcher.CreateCustomerDispute(
            Customer,
            shortDescription,
            "catalogue-change-002",
            default));
        Assert.Equal("description", length.Field);
        var valid = new global::DisputePortal.Api.Models.Domain.CreateDisputeCommand(shortDescription)
        {
            Description = "Please investigate this transaction and verify the merchant payment records."
        };
        Assert.NotEqual(Guid.Empty, (await dispatcher.CreateCustomerDispute(Customer, valid, "catalogue-change-003", default)).Id);
    }

    [Fact]
    public async Task Disabled_categories_are_not_accepted_and_schema_protects_storage_bounds()
    {
        await using var connection = await _connections.OpenConnection(default);
        await connection.ExecuteAsync(
            """
            UPDATE "configuration"."disputeCategories"
            SET "enabled" = @Disabled
            WHERE "disputeCategories"."code" = 'Duplicate'
            """,
            new { Disabled = false });
        var store = new DisputeInputPolicyStore(_connections);
        var policy = await store.GetDisputeInputPolicy(default);
        Assert.DoesNotContain(policy.Categories, c => c.Code == "Duplicate");
        await Assert.ThrowsAsync<PortalValidationException>(() => new DisputeService(_repository, store).CreateCustomerDispute(
            Customer,
            new CreateDisputeCommand
            {
                TransactionId = Transaction,
                Category = "Duplicate",
                Reason = "I was charged more than once",
                Description = "Review this duplicated payment please."
            },
            "disabled-category-001",
            default));
        await Assert.ThrowsAnyAsync<System.Data.Common.DbException>(() => connection.ExecuteAsync("UPDATE configuration.\"disputeInputPolicies\" SET \"descriptionMaximumLength\"=10000"));
    }
}
