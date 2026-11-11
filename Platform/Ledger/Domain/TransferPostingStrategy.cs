using DisputePortal.Platform.Infrastructure.Models;
using DisputePortal.Platform.Ledger.Interfaces;

namespace DisputePortal.Platform.Ledger.Domain;

public class TransferPostingStrategy : IPostingStrategy
{
    public string Kind => "Transfer";

    public PostingPlan BuildPostingPlan(PostingContext context)
    {
        var r = context.Request;
        var source = context.Source;
        var target = context.Target ?? throw new PlatformException(400, "recipient_required", "Select a recipient account.");
        LedgerAccountPolicy.RequireCurrentSource(source, r, context.Settings);
        if (target.AccountId == source.AccountId
            || target.UserId != source.UserId
            || target.Kind != "Customer"
            || target.Status != "Active")
        {
            throw new PlatformException(409, "recipient_unavailable", "Select a different active recipient account.");
        }

        if (target.Currency != source.Currency || r.QuoteId is not null)
        {
            throw new PlatformException(
                409,
                "cross_currency_transfer_unsupported",
                "Transfers must use the saved country's default currency.");
        }

        if (!DisputePortal.Platform.Contracts.CountryCurrencyPolicy.IsCurrent(context.Settings, target.CountryCode, target.Currency))
        {
            throw new PlatformException(
                409,
                "country_currency_unavailable",
                "The recipient account does not use the saved country's default currency.");
        }

        if (r.TargetAmount != r.Amount || r.TargetCurrency != target.Currency)
        {
            throw new PlatformException(400, "invalid_target_amount", "The transfer amount does not match.");
        }

        var b = new PlanBuilder(r);
        b.AddPostingLine(source.AccountId, source.Currency, "D", r.Amount + r.Fee);
        b.AddPostingLine(target.AccountId, target.Currency, "C", r.Amount);
        b.Fee(source.Currency, r.Fee);
        return b.BuildPostingPlan(true);
    }
}
