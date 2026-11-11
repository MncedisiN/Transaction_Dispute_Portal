using DisputePortal.Platform.Ledger.Domain;

namespace DisputePortal.Platform.Ledger.Interfaces;

public interface IPostingStrategy
{
    string Kind { get; }

    PostingPlan BuildPostingPlan(PostingContext context);
}
