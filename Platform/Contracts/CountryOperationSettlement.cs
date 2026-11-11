namespace DisputePortal.Platform.Contracts;

public class CountryOperationSettlement
{
    public Guid RequestId { get; set; }
    public Guid OperationId { get; set; }
    public string SourceKind { get; set; } = "";
}
