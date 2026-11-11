using DisputePortal.Platform.Contracts;
using DisputePortal.Platform.ReversalReviews.Interfaces;
using DisputePortal.Platform.Infrastructure.Interfaces;

namespace DisputePortal.Platform.ReversalReviews;

public class ReversalRequestConsumer(IReversalReviewStore store) : IDomainEventConsumer
{
    public string[] Subscriptions => ["reversal.preparation.requested"];

    public Task Handle(DomainEvent e, CancellationToken ct) => store.SaveReversalReviewRequest(e, ct);
}
