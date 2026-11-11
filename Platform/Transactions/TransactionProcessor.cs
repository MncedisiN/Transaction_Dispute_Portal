using DisputePortal.Platform.Contracts;
using DisputePortal.Platform.Infrastructure.Interfaces;
using DisputePortal.Platform.Infrastructure.Models;
using DisputePortal.Platform.Transactions.Models;
using DisputePortal.Platform.Transactions.Interfaces;

namespace DisputePortal.Platform.Transactions;

public class TransactionProcessor(ITransactionStore store, IServiceClient client, ILogger<TransactionProcessor> logger) : IPersistentJobRunner
{
    public async Task RunScheduledWork(CancellationToken ct)
    {
        for (var i = 0; i < 10; i++)
        {
            var work = await store.Claim(ct);
            if (work is null)
            {
                return;
            }

            try
            {
                await Process(work, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception error)
            {
                logger.LogWarning("Transaction {Id} retained for retry after {ErrorType}", work.Id, error.GetType().Name);
                await store.RetryTransactionProcessing(work, ct);
            }
        }
    }

    private async Task Process(WorkItem w, CancellationToken ct)
    {
        var p = w.Payment;
        if (w.Status == "Aborting")
        {
            await Abort(w, w.FailureCode ?? "payment_rejected", ct);
            return;
        }

        try
        {
            if (w.Status == "Initiated")
            {
                var account = await client.SendServiceRequest<AccountRecord>("accounts", HttpMethod.Get, "/accounts/" + p.AccountId, ct: ct);
                if (account.UserId != p.UserId
                    || account.Currency != p.Currency
                    || (p.Kind is "Refund" or "Recall" ? account.Status is not ("Active" or "Blocked") : account.Status != "Active"))
                {
                    throw new PlatformException(409, "account_unavailable", "The account is unavailable.");
                }

                if (p.Kind is "Transfer" or "Withdrawal")
                {
                    await client.SendServiceRequest<BalanceSnapshot>(
                        "ledger",
                        HttpMethod.Post,
                        "/reservations",
                        new ReserveRequest
                        {
                            TransactionId = w.Id,
                            UserId = p.UserId,
                            AccountId = p.AccountId,
                            Amount = p.Amount,
                            Fee = p.Fee,
                            Reference = p.Reference,
                            SettingsVersion = p.SettingsVersion
                        },
                        ct: ct);
                }

                await store.Transition(w, "Validated", null, ct);
                return;
            }

            if (w.Status == "Validated")
            {
                await store.Transition(w, "Processing", null, ct);
                return;
            }

            var request = new PostJournalRequest
            {
                TransactionId = w.Id,
                UserId = p.UserId,
                Kind = p.Kind,
                Reference = p.Reference,
                AccountId = p.AccountId,
                RecipientAccountId = p.RecipientAccountId,
                Amount = p.Amount,
                Fee = p.Fee,
                Currency = p.Currency,
                TargetAmount = p.TargetAmount,
                TargetCurrency = p.TargetCurrency,
                QuoteId = p.FxQuoteId,
                OriginalTransactionId = p.OriginalTransactionId,
                SettingsVersion = p.SettingsVersion
            };
            var posted = await client.SendServiceRequest<JournalResult>("ledger", HttpMethod.Post, "/journals", request, ct: ct);
            if (posted.TransactionId != w.Id)
            {
                throw new InvalidOperationException("Ledger acknowledgement does not match the transaction.");
            }

            await store.Transition(w, "Completed", null, ct);
        }
        catch (PlatformException error) when (error.Status is >= 400 and < 500 && error.Status is not (408 or 429))
        {
            await store.Transition(w, "Aborting", error.Code, ct);
        }
    }

    private async Task Abort(WorkItem w, string code, CancellationToken ct)
    {
        // Release writes a durable abort fence under the same transaction lock used by Post.
        // This settles an uncertain HTTP outcome before declaring failure.
        await client.SendServiceRequest<object>("ledger", HttpMethod.Post, $"/reservations/{w.Id}/release", ct: ct);
        try
        {
            var result = await client.SendServiceRequest<JournalResult>("ledger", HttpMethod.Get, "/journals/" + w.Id, ct: ct);
            if (result.TransactionId != w.Id)
            {
                throw new InvalidOperationException("Unexpected ledger result.");
            }

            await store.Transition(w, "Completed", null, ct);
        }
        catch (PlatformException error) when (error.Code == "journal_missing")
        {
            await store.Transition(w, "Failed", code, ct);
        }
    }
}
