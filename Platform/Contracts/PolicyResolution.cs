namespace DisputePortal.Platform.Contracts;

public class PolicyResolution<T>
    where T : PolicyVersionMetadata
{
    public PolicyResolution<T>? AtAffectedBoundary { get; set; }
    public T? Selected { get; set; }
    public DateTimeOffset EvaluationTimeUtc { get; set; }
    public DateTimeOffset? NextChangeAtUtc { get; set; }
    public string SelectionReason { get; set; } = "NoApplicablePolicy";
}
