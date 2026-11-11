using DisputePortal.Platform.Contracts;

namespace DisputePortal.Platform.ReversalReviews;

public static class ReversalReviewEligibility
{
    public static bool Eligible(ReversalPreparation r, TransactionRecord original) => original.Status == "Completed"
        && (r.Kind == "Refund"
        && original.Direction == "Debit"
        && original.Kind is not ("Refund" or "Recall")
        || r.Kind == "Recall"
        && original.Kind == "Refund");
}
