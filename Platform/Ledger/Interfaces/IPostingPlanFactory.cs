using DisputePortal.Platform.Ledger.Domain;

namespace DisputePortal.Platform.Ledger.Interfaces;

public interface IPostingPlanFactory
{
    PostingPlan CreatePostingPlan(PostingContext context);
}
