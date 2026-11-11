using DisputePortal.Platform.Contracts;
using DisputePortal.Platform.Infrastructure.Models;

namespace DisputePortal.Platform.Payments;

public static class PaymentPolicy
{
    public static void RequireAccounts(
        PaymentQuoteRequest request,
        Guid user,
        AccountRecord source,
        AccountRecord? target,
        PlatformSettings settings)
    {
        RequireTransfer(request.Kind);
        if (source.Id != request.AccountId || source.UserId != user || source.Status != "Active")
        {
            throw new PlatformException(400, "payment_invalid", "Choose an active account and payment type.");
        }

        if (!CountryCurrencyPolicy.IsCurrent(settings, source.CountryCode, source.Currency))
        {
            throw new PlatformException(409, "country_currency_unavailable", "Use the saved country's default currency for new transfers.");
        }

        if (target is null
            || target.Id != request.RecipientAccountId
            || target.Id == source.Id
            || target.UserId != user
            || target.Status != "Active")
        {
            throw new PlatformException(400, "recipient_invalid", "Choose a different active recipient account.");
        }

        if (target.Currency != source.Currency || !CountryCurrencyPolicy.IsCurrent(settings, target.CountryCode, target.Currency))
        {
            throw new PlatformException(409, "recipient_unavailable", "Choose an account in the saved country and default currency.");
        }
    }

    public static decimal ValidatePaymentPolicy(
        PaymentQuoteRequest request,
        Guid user,
        AccountRecord source,
        AccountRecord? target,
        PlatformSettings settings)
    {
        RequireAccounts(request, user, source, target, settings);
        var currency = settings.Currencies.Single(c => c.Code == source.Currency);
        var rule = settings.FinancialRules.SingleOrDefault(r => r.Country == source.CountryCode
            && r.Currency == source.Currency) ?? throw new PlatformException(
            409,
            "policy_unavailable",
            "The account's financial policy is unavailable.");
        if (request.Amount < rule.Minimum
            || request.Amount > rule.Maximum
            || decimal.Round(request.Amount, currency.Decimals) != request.Amount)
        {
            throw new PlatformException(400, "amount_invalid", "Use an amount within the configured limits and currency precision.");
        }

        var fee = decimal.Round(rule.FixedFee + request.Amount * rule.PercentageFee / 100, currency.Decimals, MidpointRounding.ToEven);
        if (source.AvailableBalance < request.Amount + fee)
        {
            throw new PlatformException(409, "insufficient_funds", "Insufficient funds for this transfer.");
        }

        return fee;
    }

    public static void RequireTransfer(string kind)
    {
        if (kind != "Transfer")
        {
            throw new PlatformException(
                409,
                "external_settlement_required",
                "Deposits and withdrawals are recorded only through the external bank integration.");
        }
    }

    public static void Match(PaymentRequest request, PaymentQuote quote, Guid user)
    {
        RequireTransfer(request.Kind);
        if (request.RequestId == Guid.Empty
            || request.QuoteId != quote.Id
            || user != quote.UserId
            || quote.Kind != request.Kind
            || quote.AccountId != request.AccountId
            || quote.RecipientAccountId != request.RecipientAccountId
            || quote.Amount != request.Amount)
        {
            throw new PlatformException(409, "quote_mismatch", "The payment does not match its quote.");
        }

        if (quote.FxQuoteId is not null || quote.TargetCurrency != quote.Currency || quote.TargetAmount != quote.Amount)
        {
            throw new PlatformException(
                409,
                "cross_currency_transfer_unsupported",
                "New transfers must retain their amount and use the saved default currency.");
        }
    }
}
