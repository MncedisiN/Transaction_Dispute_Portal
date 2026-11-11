namespace DisputePortal.Api.Models.Responses;

public class AdminQueueItemDto
{
    public DisputeSummaryDto Dispute { get; set; } = null!;
    public string CustomerName { get; set; } = string.Empty;
    public string MaskedEmail { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;
    public string TransactionReference { get; set; } = string.Empty;
    public string MerchantName { get; set; } = string.Empty;
    public DateTimeOffset TransactionAt { get; set; }
    public CaseOperationsDto? Operations { get; set; }

    public AdminQueueItemDto()
    {
    }

    public AdminQueueItemDto(
        DisputeSummaryDto Dispute,
        string CustomerName,
        string MaskedEmail,
        string AccountNumber,
        string TransactionReference,
        string MerchantName,
        DateTimeOffset TransactionAt,
        CaseOperationsDto? Operations = null)
    {
        this.Dispute = Dispute;
        this.CustomerName = CustomerName;
        this.MaskedEmail = MaskedEmail;
        this.AccountNumber = AccountNumber;
        this.TransactionReference = TransactionReference;
        this.MerchantName = MerchantName;
        this.TransactionAt = TransactionAt;
        this.Operations = Operations;
    }

    public AdminQueueItemDto(AdminQueueItemDto other)
    {
        Dispute = other.Dispute;
        CustomerName = other.CustomerName;
        MaskedEmail = other.MaskedEmail;
        AccountNumber = other.AccountNumber;
        TransactionReference = other.TransactionReference;
        MerchantName = other.MerchantName;
        TransactionAt = other.TransactionAt;
        Operations = other.Operations;
    }
}
