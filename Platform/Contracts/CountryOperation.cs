namespace DisputePortal.Platform.Contracts;

public class CountryOperation
{
    public Guid RequestId { get; set; }
    public Guid OperationId { get; set; }
    public string SourceKind { get; set; } = "";
    public int PolicyVersion { get; set; }
    public string CountryCode { get; set; } = "";
    public string Currency { get; set; } = "";
    public Guid? OriginalTransactionId { get; set; }
    public Guid? CustomerId { get; set; }
    public DateTimeOffset? SettledAt { get; set; }
}
