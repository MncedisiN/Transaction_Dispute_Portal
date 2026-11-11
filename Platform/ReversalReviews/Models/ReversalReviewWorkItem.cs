using System.Text.Json;
using DisputePortal.Platform.Contracts;

namespace DisputePortal.Platform.ReversalReviews.Models;

public class ReversalReviewWorkItem
{
    public Guid Id { get; set; }
    public string Body { get; set; } = "";
    public Guid LeaseId { get; set; }

    public ReversalReviewWorkItem()
    {
    }

    public ReversalReviewWorkItem(Guid id, string body, Guid leaseId)
    {
        Id = id;
        Body = body;
        LeaseId = leaseId;
    }

    public ReversalPreparation Request => JsonSerializer.Deserialize<ReversalPreparation>(Body, DomainEvent.Json)!;
}
