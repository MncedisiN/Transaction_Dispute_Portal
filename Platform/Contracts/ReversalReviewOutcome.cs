namespace DisputePortal.Platform.Contracts;

public class ReversalReviewOutcome
{
    public Guid RequestId { get; set; }
    public Guid OriginalTransactionId { get; set; }
    public Guid ActorId { get; set; }
    public string Kind { get; set; } = "";
    public string Status { get; set; } = "";
    public string? Code { get; set; }
}
