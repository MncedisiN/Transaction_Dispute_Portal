namespace DisputePortal.Api.Models.Responses;

public class AdminDisputeDto
{
    public DisputeSummaryDto Dispute { get; set; } = null!;
    public string CustomerName { get; set; } = string.Empty;
    public string MaskedEmail { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;
    public string TransactionReference { get; set; } = string.Empty;
    public string MerchantName { get; set; } = string.Empty;
    public DateTimeOffset TransactionAt { get; set; }
    public IReadOnlyList<StatusHistoryDto> History { get; set; } = [];
    public IReadOnlyList<DisputeNoteDto> Notes { get; set; } = [];
    public CaseOperationsDto? Operations { get; set; }
    public ActionActorDto? ResolvedBy { get; set; }

    public AdminDisputeDto()
    {
    }

    public AdminDisputeDto(
        DisputeSummaryDto Dispute,
        string CustomerName,
        string MaskedEmail,
        string AccountNumber,
        string TransactionReference,
        string MerchantName,
        DateTimeOffset TransactionAt,
        IReadOnlyList<StatusHistoryDto> History,
        IReadOnlyList<DisputeNoteDto> Notes,
        CaseOperationsDto? Operations = null,
        ActionActorDto? ResolvedBy = null)
    {
        this.Dispute = Dispute;
        this.CustomerName = CustomerName;
        this.MaskedEmail = MaskedEmail;
        this.AccountNumber = AccountNumber;
        this.TransactionReference = TransactionReference;
        this.MerchantName = MerchantName;
        this.TransactionAt = TransactionAt;
        this.History = History;
        this.Notes = Notes;
        this.Operations = Operations;
        this.ResolvedBy = ResolvedBy;
    }

    public AdminDisputeDto(AdminDisputeDto other)
    {
        Dispute = other.Dispute;
        CustomerName = other.CustomerName;
        MaskedEmail = other.MaskedEmail;
        AccountNumber = other.AccountNumber;
        TransactionReference = other.TransactionReference;
        MerchantName = other.MerchantName;
        TransactionAt = other.TransactionAt;
        History = other.History;
        Notes = other.Notes;
        Operations = other.Operations;
        ResolvedBy = other.ResolvedBy;
    }
}
