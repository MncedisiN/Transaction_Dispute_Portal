using DisputePortal.Platform.Contracts;

namespace DisputePortal.Platform.Ledger.Domain;

public class PostingContext
{
    public PostJournalRequest Request { get; set; } = null!;
    public LedgerPosition Source { get; set; } = null!;
    public LedgerPosition? Target { get; set; }
    public PlatformSettings Settings { get; set; } = null!;
}
