namespace DisputePortal.Api.Models.Responses;

public class DisputeDetailDto
{
    public DisputeSummaryDto Dispute { get; set; } = null!;
    public string? TransactionReference { get; set; }
    public string MerchantName { get; set; } = string.Empty;
    public DateTimeOffset TransactionAt { get; set; }
    public IReadOnlyList<StatusHistoryDto> History { get; set; } = [];
    public IReadOnlyList<DisputeNoteDto> Notes { get; set; } = [];
    public ActionActorDto? ResolvedBy { get; set; }

    public DisputeDetailDto()
    {
    }

    public DisputeDetailDto(
        DisputeSummaryDto Dispute,
        string? TransactionReference,
        string MerchantName,
        DateTimeOffset TransactionAt,
        IReadOnlyList<StatusHistoryDto> History,
        IReadOnlyList<DisputeNoteDto> Notes,
        ActionActorDto? ResolvedBy = null)
    {
        this.Dispute = Dispute;
        this.TransactionReference = TransactionReference;
        this.MerchantName = MerchantName;
        this.TransactionAt = TransactionAt;
        this.History = History;
        this.Notes = Notes;
        this.ResolvedBy = ResolvedBy;
    }
}
