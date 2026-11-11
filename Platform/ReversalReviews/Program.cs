using DisputePortal.Platform.Infrastructure;
using DisputePortal.Platform.ReversalReviews;

await ServiceHost.RunServiceHost<ReversalReviewsModule>("debt", args);
