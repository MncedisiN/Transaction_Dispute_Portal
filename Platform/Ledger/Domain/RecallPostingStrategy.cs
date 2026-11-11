using DisputePortal.Platform.Infrastructure.Models;
using DisputePortal.Platform.Ledger.Interfaces;

namespace DisputePortal.Platform.Ledger.Domain;

public class RecallPostingStrategy : IPostingStrategy
{
    public string Kind => "Recall";

    public PostingPlan BuildPostingPlan(PostingContext context)
    {
        var r = context.Request;
        if (r.OriginalTransactionId is null || r.Fee != 0)
        {
            throw new PlatformException(400, "original_required", "A recall needs its original refund and cannot add a fee.");
        }

        LedgerAccountPolicy.RequireHistoricalSource(context.Source, r);
        var b = new PlanBuilder(r);
        b.AddPostingLine(r.AccountId, r.Currency, "D", r.Amount);
        b.AddPostingLine(b.Clearing(r.Currency, "Refund"), r.Currency, "C", r.Amount);
        return b.BuildPostingPlan(false);
    }
}
