namespace DisputePortal.Api.Models.Responses;

public class PendingFinancialDto
{
    public Guid TransactionId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? FailureCode { get; set; }

    public PendingFinancialDto()
    {
    }

    public PendingFinancialDto(Guid TransactionId, string Kind, string Status, string? FailureCode)
    {
        this.TransactionId = TransactionId;
        this.Kind = Kind;
        this.Status = Status;
        this.FailureCode = FailureCode;
    }
}
