using DisputePortal.Platform.Contracts;
using DisputePortal.Platform.ReversalReviews.Models;

namespace DisputePortal.Platform.ReversalReviews.Interfaces;

public interface IReversalReviewStore
{
    Task SaveReversalReviewRequest(DomainEvent e, CancellationToken ct);

    Task<ReversalReviewWorkItem?> ClaimNextReversalReview(CancellationToken ct);

    Task CompleteReversalReview(ReversalReviewWorkItem work, string status, string code, CancellationToken ct);

    Task RetryReversalReview(ReversalReviewWorkItem work, CancellationToken ct);

    Task<object> GetReversalReviews(CancellationToken ct);
}
