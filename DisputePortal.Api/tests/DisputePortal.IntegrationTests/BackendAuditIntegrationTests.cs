using DisputePortal.Api.Models.Domain;
using DisputePortal.Api.Service.Disputes;
using Xunit;

namespace DisputePortal.IntegrationTests;

public sealed partial class DisputeWorkflowRepositoryTests
{
    [Fact]
    public async Task Large_valid_dispute_page_is_empty_without_overflowing_sql_offset()
    {
        var ct = CancellationToken.None;
        await _repository.CreateDispute(
            Customer,
            new CreateDisputeCommand
            {
                TransactionId = Transaction,
                Category = "Duplicate",
                Reason = "The payment was charged twice",
                Description = "Investigate this duplicate payment."
            },
            "large-page-regression",
            ct);
        var page = await _repository.GetCustomerDisputes(Customer, int.MaxValue, 100, ct);
        Assert.Empty(page.Items);
        Assert.Equal(1, page.Total);
    }
}
