using DisputePortal.Platform.Contracts;
using DisputePortal.Platform.ReversalReviews.Interfaces;
using DisputePortal.Platform.Infrastructure.Interfaces;
using DisputePortal.Platform.Infrastructure.Models;

namespace DisputePortal.Platform.ReversalReviews;

public class ScheduledReversalReviewWorker(IReversalReviewStore store, IServiceClient client) : IPersistentJobRunner
{
    public async Task RunScheduledWork(CancellationToken ct)
    {
        var w = await store.ClaimNextReversalReview(ct);
        if (w is null)
        {
            return;
        }

        try
        {
            var r = w.Request;
            var original = await client.SendServiceRequest<TransactionRecord>(
                "transactions",
                HttpMethod.Get,
                "/transactions/" + r.OriginalTransactionId,
                ct: ct);
            if (!ReversalReviewEligibility.Eligible(r, original))
            {
                await store.CompleteReversalReview(w, "Ineligible", "original_ineligible", ct);
                return;
            }

            await client.SendServiceRequest<TransactionRecord>("transactions", HttpMethod.Post, "/reversals", r, ct: ct);
            await store.CompleteReversalReview(w, "Requested", "transaction_requested", ct);
        }
        catch (PlatformException e) when (e.Status is >= 400 and < 500 && e.Status is not (408 or 429))
        {
            await store.CompleteReversalReview(w, "Ineligible", e.Code, ct);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            await store.RetryReversalReview(w, ct);
        }
    }
}
