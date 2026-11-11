using DisputePortal.Platform.Contracts;
using DisputePortal.Platform.Infrastructure.Models;

namespace DisputePortal.Platform.Ledger;

public static class ReversalPolicy
{
    public static void ValidateReversalRequest(PostJournalRequest current, PostJournalRequest original)
    {
        if (current.OriginalTransactionId is null
            || original.TransactionId != current.OriginalTransactionId
            || current.Fee != 0
            || original.UserId != current.UserId
            || original.AccountId != current.AccountId
            || original.Currency != current.Currency
            || original.Amount != current.Amount)
        {
            throw new PlatformException(
                409,
                "reversal_mismatch",
                "The reversal must exactly match its original account, currency and amount.");
        }

        if (current.Kind == "Refund" && original.Kind is not ("Transfer" or "Withdrawal" or "ExternalDebit"))
        {
            throw new PlatformException(409, "refund_ineligible", "Only an eligible original debit can be refunded.");
        }

        if (current.Kind == "Recall" && original.Kind != "Refund")
        {
            throw new PlatformException(409, "recall_ineligible", "Only a posted refund may be recalled.");
        }
    }
}
