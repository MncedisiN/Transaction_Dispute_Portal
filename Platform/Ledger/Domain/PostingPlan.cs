using DisputePortal.Platform.Contracts;

namespace DisputePortal.Platform.Ledger.Domain;

public class PostingPlan
{
    public PostJournalRequest Request { get; set; } = null!;
    public IReadOnlyList<PostingLine> Lines { get; set; } = null!;
    public IReadOnlyList<ClearingAccount> ClearingAccounts { get; set; } = null!;
    public bool RequiresReservation { get; set; }
}
