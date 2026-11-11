namespace DisputePortal.Platform.Contracts;

public class CountryOperationRequest
{
    public Guid RequestId { get; set; }
    public Guid? OperationId { get; set; }
    public string SourceKind { get; set; } = "";
    public string CountryCode { get; set; } = "";
    public string Currency { get; set; } = "";
    public int? ExpectedVersion { get; set; }
    public Guid? OriginalTransactionId { get; set; }
    public Guid? CustomerId { get; set; }
}
