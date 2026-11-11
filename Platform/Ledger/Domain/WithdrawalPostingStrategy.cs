using DisputePortal.Platform.Ledger.Interfaces;

namespace DisputePortal.Platform.Ledger.Domain;

public class WithdrawalPostingStrategy : IPostingStrategy
{
    public string Kind => "Withdrawal";

    public PostingPlan BuildPostingPlan(PostingContext context)
    {
        var r = context.Request;
        var a = context.Source;
        LedgerAccountPolicy.RequireCurrentSource(a, r, context.Settings);
        var b = new PlanBuilder(r);
        b.AddPostingLine(a.AccountId, a.Currency, "D", r.Amount + r.Fee);
        b.AddPostingLine(b.Clearing(a.Currency, "External"), a.Currency, "C", r.Amount);
        b.Fee(a.Currency, r.Fee);
        return b.BuildPostingPlan(true);
    }
}
