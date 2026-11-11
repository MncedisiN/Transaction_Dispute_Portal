namespace DisputePortal.Api.Observability;

public class MetricBatch
{
    public Guid Id { get; set; }
    public IReadOnlyList<MetricSummary> Summaries { get; set; } = null!;
}
