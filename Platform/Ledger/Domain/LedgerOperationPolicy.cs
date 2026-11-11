using DisputePortal.Platform.Contracts;
using DisputePortal.Platform.Infrastructure.Models;

namespace DisputePortal.Platform.Ledger.Domain;

public static class LedgerOperationPolicy
{
    public static void RequireAccount(CountryOperation operation, AccountEvent account)
    {
        if (operation.SourceKind is not ("Account" or "Connection")
            || operation.SourceKind == "Account"
            && operation.CustomerId != account.UserId
            || operation.OperationId != account.AccountId
            || operation.SettledAt is not null
            || operation.PolicyVersion <= 0
            || operation.CountryCode != account.CountryCode
            || operation.Currency != account.Currency)
        {
            throw new PlatformException(
                409,
                "country_operation_required",
                "A matching unfinished account policy operation is required before activation.");
        }
    }

    public static void Require(
        CountryOperation operation,
        Guid operationId,
        LedgerPosition source,
        bool historicalSettlement,
        Guid? originalTransactionId = null)
    {
        var permittedKind = historicalSettlement ? operation.SourceKind is "Reversal" or "DisputeFinance" : operation.SourceKind == "Payment";
        if (!permittedKind
            || !historicalSettlement
            && operation.CustomerId != source.UserId
            || operation.OperationId != operationId
            || operation.SettledAt is not null
            || operation.PolicyVersion <= 0
            || operation.CountryCode != source.CountryCode
            || operation.Currency != source.Currency
            || historicalSettlement
            && (originalTransactionId is null
            || operation.OriginalTransactionId != originalTransactionId))
        {
            throw new PlatformException(
                409,
                "country_operation_required",
                "A matching unfinished country-policy operation is required before posting.");
        }
    }
}
