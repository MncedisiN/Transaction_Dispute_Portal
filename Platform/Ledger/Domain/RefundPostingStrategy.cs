using DisputePortal.Platform.Infrastructure.Models;
using DisputePortal.Platform.Ledger.Interfaces;

namespace DisputePortal.Platform.Ledger.Domain;

public class RefundPostingStrategy : IPostingStrategy
{
    public string Kind => "Refund";

    public PostingPlan BuildPostingPlan(PostingContext context)
    {
        var r = context.Request;
        if (r.OriginalTransactionId is null || r.Fee != 0)
        {
            throw new PlatformException(400, "original_required", "A refund needs its original transaction and cannot add a fee.");
        }

        LedgerAccountPolicy.RequireHistoricalSource(context.Source, r);
        var b = new PlanBuilder(r);
        b.AddPostingLine(b.Clearing(r.Currency, "Refund"), r.Currency, "D", r.Amount);
        b.AddPostingLine(r.AccountId, r.Currency, "C", r.Amount);
        return b.BuildPostingPlan(false);
    }
}
