using DisputePortal.Platform.Contracts;
using DisputePortal.Platform.Infrastructure.Models;

namespace DisputePortal.Platform.Ledger.Domain;

public static class LedgerAccountPolicy
{
    public static void RequireCurrentSource(LedgerPosition source, PostJournalRequest request, PlatformSettings settings)
    {
        if (source.AccountId != request.AccountId
            || source.UserId != request.UserId
            || source.Kind != "Customer"
            || source.Status != "Active"
            || source.Currency != request.Currency)
        {
            throw new PlatformException(409, "account_unavailable", "The source account is unavailable.");
        }

        if (!CountryCurrencyPolicy.IsCurrent(settings, source.CountryCode, source.Currency))
        {
            throw new PlatformException(
                409,
                "country_currency_unavailable",
                "The account does not use the saved country's default currency.");
        }
    }

    public static void RequireHistoricalSource(LedgerPosition source, PostJournalRequest request)
    {
        if (source.AccountId != request.AccountId
            || source.UserId != request.UserId
            || source.Kind != "Customer"
            || source.Currency != request.Currency
            || source.Status is not ("Active" or "Blocked"))
        {
            throw new PlatformException(409, "account_unavailable", "The original financial account is unavailable.");
        }
    }
}
