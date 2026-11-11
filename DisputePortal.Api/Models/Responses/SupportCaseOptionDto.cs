namespace DisputePortal.Api.Models.Responses;

public class SupportCaseOptionDto
{
    public Guid Id { get; set; }
    public string Reference { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string TransactionDescription { get; set; } = string.Empty;
    public string MerchantName { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }

    public SupportCaseOptionDto()
    {
    }

    public SupportCaseOptionDto(
        Guid Id,
        string Reference,
        string Status,
        string TransactionDescription,
        string MerchantName,
        DateTimeOffset CreatedAt)
    {
        this.Id = Id;
        this.Reference = Reference;
        this.Status = Status;
        this.TransactionDescription = TransactionDescription;
        this.MerchantName = MerchantName;
        this.CreatedAt = CreatedAt;
    }
}
