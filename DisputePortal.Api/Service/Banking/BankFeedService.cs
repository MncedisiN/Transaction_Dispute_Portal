using DisputePortal.Api.Models.Banking;
using DisputePortal.Api.Models.Exceptions;
using DisputePortal.Api.Models.Responses;
using DisputePortal.Api.Models.Domain;
using DisputePortal.Api.Interfaces.Repositories;
using DisputePortal.Api.Interfaces.ExternalServices;

namespace DisputePortal.Api.Service.Banking;

public class BankFeedService(IBankFeedStore store, IEnumerable<IBankTransactionConnector> connectors)
{
    private readonly IReadOnlyDictionary<string, IBankTransactionConnector> _connectors = connectors.ToDictionary(
        c => c.Provider,
        StringComparer.Ordinal);

    public async Task<AccountFeedDto> GetStatus(Guid customer, Guid account, CancellationToken ct)
    {
        var binding = await store.GetOwnedBinding(customer, account, ct);
        return new(binding != null, binding != null && _connectors.ContainsKey(binding.Provider), binding?.Provider, binding?.LastSyncedAt);
    }

    public async Task<AccountRefreshDto> Refresh(Guid customer, Guid account, CancellationToken ct)
    {
        var processed = 0;
        var more = false;
        DateTimeOffset? synced = null;
        // Bounded work; a scheduler can resume the saved cursor if more pages remain.
        for (var pageNumber = 0; pageNumber < 5; pageNumber++)
        {
            ct.ThrowIfCancellationRequested();
            var binding = await store.GetOwnedBinding(customer, account, ct) ?? throw new PortalConflictException("This account has no connected banking feed.");
            if (!_connectors.TryGetValue(binding.Provider, out var connector))
            {
                throw new PortalConflictException("The account connection is not available yet.");
            }

            var page = await connector.ReadTransactions(binding.ExternalAccountId, binding.Cursor, 100, ct);
            ValidatePage(binding, page);
            processed += await store.ApplyPage(customer, binding, page, ct);
            synced = (await store.GetOwnedBinding(customer, account, ct))?.LastSyncedAt;
            more = page.HasMore;
            if (!more)
            {
                break;
            }
        }

        return new(processed, more, synced);
    }

    public static void ValidatePage(BankFeedBinding binding, BankTransactionPage page)
    {
        if (page is null
            || page.Items is null
            || page.Items.Any(x => x is null)
            || page.ExternalAccountId != binding.ExternalAccountId
            || page.Items.Count > 100
            || page.Items.Select(x => x.ExternalId).Distinct(StringComparer.Ordinal).Count() != page.Items.Count)
        {
            throw new PortalValidationException("The banking feed returned an invalid account or transaction page.");
        }

        if (page.NextCursor?.Length > 2000
            || (page.HasMore
            && (string.IsNullOrWhiteSpace(page.NextCursor)
            || page.NextCursor == binding.Cursor)))
        {
            throw new PortalValidationException("The banking feed did not advance its cursor.");
        }

        foreach (var item in page.Items)
        {
            if (string.IsNullOrWhiteSpace(item.ExternalId)
                || item.ExternalId.Length > 200
                || string.IsNullOrWhiteSpace(item.Description)
                || item.Description.Length > 500
                || string.IsNullOrWhiteSpace(item.MerchantName)
                || item.MerchantName.Length > 200
                || item.Amount < 0
                || item.Amount >= 10000000000000000m
                || decimal.Round(item.Amount, 2) != item.Amount
                || string.IsNullOrWhiteSpace(item.Currency)
                || item.Currency.Length != 3
                || !item.Currency.All(c => c is >= 'A' and <= 'Z')
                || item.Type is not ("CardPurchase" or "CreditCardPayment" or "DebitOrder" or "Transfer" or "CashWithdrawal" or "Deposit" or "Fee" or "Other")
                || item.Direction is not ("Debit" or "Credit")
                || item.Status is not ("Posted" or "Pending" or "Reversed")
                || item.DebitOrderScheme is not (null or "EFT" or "RM" or "DebiCheck")
                || item.MandateStatus is not (null or "Unknown" or "Valid" or "Disputed"))
            {
                throw new PortalValidationException("The banking feed returned an unsupported transaction.");
            }
        }
    }
}
