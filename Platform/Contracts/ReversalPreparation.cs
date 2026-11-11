namespace DisputePortal.Platform.Contracts;

public class ReversalPreparation
{
    public Guid RequestId { get; set; }
    public Guid OriginalTransactionId { get; set; }
    public string Kind { get; set; } = null!;
    public Guid ActorId { get; set; }
}
