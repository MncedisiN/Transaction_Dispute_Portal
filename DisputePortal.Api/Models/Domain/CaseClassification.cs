namespace DisputePortal.Api.Models.Domain;

public class CaseClassification
{
    public string TransactionType { get; set; } = null!;
    public string QueueType { get; set; } = null!;
    public string Severity { get; set; } = null!;
    public int Priority { get; set; }
}
