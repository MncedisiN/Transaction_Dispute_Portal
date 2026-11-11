using DisputePortal.Platform.Infrastructure.Models;
using DisputePortal.Platform.Ledger.Interfaces;

namespace DisputePortal.Platform.Ledger.Domain;

public class DepositPostingStrategy : IPostingStrategy
{
    public string Kind => "Deposit";

    public PostingPlan BuildPostingPlan(PostingContext context)
    {
        var r = context.Request;
        var a = context.Source;
        LedgerAccountPolicy.RequireCurrentSource(a, r, context.Settings);
        if (r.Fee >= r.Amount)
        {
            throw new PlatformException(400, "fee_exceeds_amount", "The fee exceeds the deposit.");
        }

        var b = new PlanBuilder(r);
        b.AddPostingLine(b.Clearing(a.Currency, "External"), a.Currency, "D", r.Amount);
        b.AddPostingLine(a.AccountId, a.Currency, "C", r.Amount - r.Fee);
        b.Fee(a.Currency, r.Fee);
        return b.BuildPostingPlan(false);
    }
}
