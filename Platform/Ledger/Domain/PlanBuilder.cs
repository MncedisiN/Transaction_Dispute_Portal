using System.Security.Cryptography;
using System.Text;
using DisputePortal.Platform.Contracts;

namespace DisputePortal.Platform.Ledger.Domain;

internal class PlanBuilder(PostJournalRequest request)
{
    private readonly List<PostingLine> lines = [];
    private readonly Dictionary<Guid, ClearingAccount> clearing = [];

    public Guid Clearing(string currency, string purpose)
    {
        var id = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes("DisputePortal/" + purpose + "/" + currency))[..16]);
        clearing.TryAdd(
            id,
            new global::DisputePortal.Platform.Ledger.Domain.ClearingAccount
            {
                Id = id,
                Currency = currency,
                Purpose = purpose
            });
        return id;
    }

    public void AddPostingLine(Guid account, string currency, string side, decimal amount)
    {
        if (amount > 0)
        {
            lines.Add(new global::DisputePortal.Platform.Ledger.Domain.PostingLine
            {
                Id = Guid.NewGuid(),
                AccountId = account,
                Currency = currency,
                Side = side,
                Amount = amount
            });
        }
    }

    public void Fee(string currency, decimal fee)
    {
        if (fee > 0)
        {
            AddPostingLine(Clearing(currency, "Fees"), currency, "C", fee);
        }
    }

    public PostingPlan BuildPostingPlan(bool reserve) => new global::DisputePortal.Platform.Ledger.Domain.PostingPlan
    {
        Request = request,
        Lines = lines,
        ClearingAccounts = clearing.Values.ToArray(),
        RequiresReservation = reserve
    };
}
