using DisputePortal.Platform.Contracts;
using DisputePortal.Platform.Infrastructure.Interfaces;
using DisputePortal.Platform.Infrastructure.Models;
using DisputePortal.Platform.Payments.Interfaces;

namespace DisputePortal.Platform.Payments;

public class PaymentService(IPaymentStore store, IServiceClient client, ILogger<PaymentService> logger, DisputePortal.Platform.Infrastructure.ServiceOptions options) : IPaymentService, IPersistentJobRunner
{
    public async Task<PaymentQuote> Quote(PaymentQuoteRequest request, Guid user, CancellationToken ct)
    {
        PaymentPolicy.RequireTransfer(request.Kind);
        var settings = await Settings(ct);
        var source = await Account(request.AccountId, ct);
        var target = request.RecipientAccountId is { } id ? await Account(id, ct) : null;
        if (source.UserId != user || target?.UserId != user)
        {
            throw new PlatformException(404, "account_missing", "Choose your own source and destination accounts.");
        }

        var balances = await client.SendServiceRequest<BalanceSnapshot[]>("ledger", HttpMethod.Get, "/owners/" + user + "/balances", ct: ct);
        var balance = balances.SingleOrDefault(value => value.AccountId == source.Id) ?? throw new PlatformException(
            409,
            "account_unavailable",
            "The source account is not ready.");
        source = new global::DisputePortal.Platform.Contracts.AccountRecord(source)
        {
            LedgerBalance = balance.LedgerBalance,
            ReservedBalance = balance.ReservedBalance,
            Version = balance.Version
        };
        var fee = PaymentPolicy.ValidatePaymentPolicy(request, user, source, target, settings);
        var quote = new PaymentQuote
        {
            Id = Guid.NewGuid(),
            UserId = user,
            Kind = request.Kind,
            AccountId = source.Id,
            RecipientAccountId = target!.Id,
            Amount = request.Amount,
            Currency = source.Currency,
            Fee = fee,
            TargetAmount = request.Amount,
            TargetCurrency = source.Currency,
            FxQuoteId = null,
            SettingsVersion = settings.Version,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(settings.QuoteSeconds)
        };
        await store.SaveQuote(quote, ct);
        return quote;
    }

    public async Task<PaymentRecord> CreateInternalTransfer(PaymentRequest request, Guid user, CancellationToken ct)
    {
        PaymentPolicy.RequireTransfer(request.Kind);
        var record = await store.Replay(user, request, ct);
        if (record is null)
        {
            var quote = await store.ReadQuote(user, request, ct);
            var settings = await Settings(ct);
            var source = await Account(request.AccountId, ct);
            var target = request.RecipientAccountId is { } id ? await Account(id, ct) : null;
            PaymentPolicy.RequireAccounts(
                new(request.Kind, request.AccountId, request.RecipientAccountId, request.Amount),
                user,
                source,
                target,
                settings);
            try
            {
                var operation = await client.SendServiceRequest<CountryOperation>(
                    "configuration",
                    HttpMethod.Post,
                    "/country-operations",
                    new CountryOperationRequest
                    {
                        RequestId = request.RequestId,
                        SourceKind = "Payment",
                        CustomerId = user,
                        ExpectedVersion = quote.SettingsVersion,
                        CountryCode = source.CountryCode,
                        Currency = source.Currency
                    },
                    ct: ct);
                record = await store.InsertPaymentIntent(user, request, operation, ct);
            }
            catch
            {
                // A committed request tombstone fences every concurrent/late local create before settlement.
                using var recovery = new CancellationTokenSource(TimeSpan.FromSeconds(options.CleanupTimeoutSeconds));
                try
                {
                    var outcome = await store.CancelUnknown(user, request.RequestId, recovery.Token);
                    if (outcome.Cancelled)
                    {
                        await Settle(user, request.RequestId, recovery.Token);
                    }
                }
                catch (Exception cleanupError)
                {
                    logger.LogWarning(cleanupError, "Payment request cleanup remains pending for request {RequestId}", request.RequestId);
                }

                throw;
            }
        }

        if (record.Status == "Failed")
        {
            await FinishFailure(record, ct);
            throw new PlatformException(
                409,
                record.FailureCode ?? "transfer_failed",
                record.FailureCode == "insufficient_funds" ? "Insufficient funds for this transfer." : "This transfer could not proceed. Review a new quote.");
        }

        return record.Status == "Preparing" ? await PrepareTransferPosting(record, ct) : record;
    }

    public async Task<PaymentRequestOutcome> CancelUnknown(Guid user, Guid request, CancellationToken ct)
    {
        var outcome = await store.CancelUnknown(user, request, ct);
        if (outcome.Cancelled)
        {
            await Settle(user, request, ct);
        }
        else if (outcome.Payment is { Status: "Failed" } failed)
        {
            await FinishFailure(failed, ct);
        }

        return outcome;
    }

    private Task<PlatformSettings> Settings(CancellationToken ct) => client.SendServiceRequest<PlatformSettings>(
        "configuration",
        HttpMethod.Get,
        "/settings",
        ct: ct);

    private Task<AccountRecord> Account(Guid id, CancellationToken ct) => client.SendServiceRequest<AccountRecord>(
        "accounts",
        HttpMethod.Get,
        "/accounts/" + id,
        ct: ct);

    private Task<object> Settle(Guid user, Guid requestId, CancellationToken ct) => client.SendServiceRequest<object>(
        "configuration",
        HttpMethod.Post,
        "/country-operations/Payment/" + requestId + "/settle?customerId=" + user,
        ct: ct);

    private async Task<bool> FinishFailure(PaymentRecord payment, CancellationToken ct)
    {
        if (!await store.Fail(payment.Id, payment.FailureCode ?? "transfer_failed", ct))
        {
            return false;
        }

        await client.SendServiceRequest<object>("ledger", HttpMethod.Post, "/reservations/" + payment.Id + "/release", ct: ct);
        await store.FinishFailure(payment.Id, ct);
        await Settle(payment.UserId, payment.RequestId, ct);
        return true;
    }

    private async Task<PaymentRecord> PrepareTransferPosting(PaymentRecord payment, CancellationToken ct)
    {
        try
        {
            if (payment.QuoteExpiresAt is { } expires && expires <= DateTimeOffset.UtcNow)
            {
                throw new PlatformException(409, "quote_expired", "Review a new transfer quote.");
            }

            var settings = await Settings(ct);
            var source = await Account(payment.AccountId, ct);
            var target = payment.RecipientAccountId is { } id ? await Account(id, ct) : null;
            PaymentPolicy.RequireAccounts(
                new(payment.Kind, payment.AccountId, payment.RecipientAccountId, payment.Amount),
                payment.UserId,
                source,
                target,
                settings);
            if (payment.FxQuoteId is not null
                || payment.Currency != source.Currency
                || payment.TargetCurrency != source.Currency
                || payment.TargetAmount != payment.Amount)
            {
                throw new PlatformException(
                    409,
                    "cross_currency_transfer_unsupported",
                    "New transfers must retain the same amount and saved default currency.");
            }

            var balance = await client.SendServiceRequest<BalanceSnapshot>(
                "ledger",
                HttpMethod.Post,
                "/reservations",
                new ReserveRequest
                {
                    TransactionId = payment.Id,
                    UserId = payment.UserId,
                    AccountId = payment.AccountId,
                    Amount = payment.Amount,
                    Fee = payment.Fee,
                    Reference = payment.Reference,
                    SettingsVersion = payment.SettingsVersion
                },
                ct: ct);
            var accepted = await store.Activate(payment.Id, ct);
            if (accepted.Status == "Failed")
            {
                throw new PlatformException(
                    409,
                    accepted.FailureCode ?? "transfer_failed",
                    "This transfer was rejected. Review a new quote.");
            }

            return new global::DisputePortal.Platform.Contracts.PaymentRecord(accepted)
            {
                Balances = [balance]
            };
        }
        catch (PlatformException error) when (error.Status is >= 400 and < 500 && error.Status is not (408 or 429))
        {
            if (!await FinishFailure(new global::DisputePortal.Platform.Contracts.PaymentRecord(payment) { FailureCode = error.Code }, ct))
            {
                var current = await store.Lookup(payment.UserId, payment.RequestId, ct);
                if (current.Status != "Failed")
                {
                    return current;
                }

                await FinishFailure(current, ct);
            }

            throw;
        }
    }

    public async Task RunScheduledWork(CancellationToken ct)
    {
        foreach (var payment in await store.Preparing(ct))
        {
            try
            {
                if (payment.Status == "Failed")
                {
                    await FinishFailure(payment, ct);
                }
                else
                {
                    await PrepareTransferPosting(payment, ct);
                }
            }
            catch (PlatformException error) when (error.Status is >= 400 and < 500 && error.Status is not (408 or 429))
            {
            }
        }
    }
}
