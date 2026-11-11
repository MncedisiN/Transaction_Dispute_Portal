using DisputePortal.Platform.Contracts;
using DisputePortal.Platform.Ledger.Domain;
using DisputePortal.Platform.Ledger.Interfaces;

namespace DisputePortal.Platform.Ledger;

public class LedgerCommandService(
    ILedgerCommandStore writes,
    ILedgerQueryStore reads,
    IFinancialConfiguration configuration,
    IPostingPlanFactory plans) : ILedgerCommands
{
    public async Task<BalanceSnapshot> ReserveAccountFunds(ReserveRequest request, CancellationToken ct)
    {
        var operation = await configuration.RequireOperation(request.TransactionId, ct);
        var settings = await configuration.GetFinancialConfiguration(ct);
        var source = await reads.ReadPosition(request.AccountId, ct);
        LedgerOperationPolicy.Require(operation, request.TransactionId, source, false);
        return await writes.ReserveAccountFunds(request, settings, ct);
    }

    public async Task<JournalResult> Post(PostJournalRequest request, CancellationToken ct)
    {
        var replay = await reads.TryPosted(request, ct);
        if (replay is not null)
        {
            return replay;
        }

        var operation = await configuration.RequireOperation(request.TransactionId, ct);
        var settings = await configuration.GetFinancialConfiguration(ct);
        var source = await reads.ReadPosition(request.AccountId, ct);
        LedgerOperationPolicy.Require(
            operation,
            request.TransactionId,
            source,
            request.Kind is "Refund" or "Recall",
            request.OriginalTransactionId);
        var target = request.RecipientAccountId is { } id ? await reads.ReadPosition(id, ct) : null;
        var plan = plans.CreatePostingPlan(new global::DisputePortal.Platform.Ledger.Domain.PostingContext
        {
            Request = request,
            Source = source,
            Target = target,
            Settings = settings
        });
        return await writes.Post(plan, settings, ct);
    }

    public Task ReleaseReservedAccountFunds(Guid id, CancellationToken ct) => writes.ReleaseReservedAccountFunds(id, ct);
}
