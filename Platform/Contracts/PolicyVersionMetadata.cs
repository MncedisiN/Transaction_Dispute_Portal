namespace DisputePortal.Platform.Contracts;
// Shared publication facts, not an arbitrary configuration/rules engine.
public class PolicyVersionMetadata
{
    public Guid PolicyPublicId { get; set; }
    public Guid VersionPublicId { get; set; }
    public int Version { get; set; }
    public long ConcurrencyToken { get; set; }
    public string Scope { get; set; } = "Global";
    public int Priority { get; set; }
    public DateTimeOffset EffectiveFromUtc { get; set; }
    public DateTimeOffset? EffectiveToUtc { get; set; }
    public DateTimeOffset? CreatedAtUtc { get; set; }
    public Guid? CreatedBy { get; set; }
    public string? CreatedByName { get; set; }
    public DateTimeOffset? PublishedAtUtc { get; set; }
    public string ChangeReason { get; set; } = "";
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAtUtc { get; set; }
    public Guid? DeletedBy { get; set; }
    public string? DeletedByName { get; set; }
    public string? DeletionReason { get; set; }
    public string Provenance { get; set; } = "Published";
    public Guid? RequestId { get; set; }
    public Guid? SourceVersionPublicId { get; set; }
    public string State { get; set; } = "";
    public DateTimeOffset EvaluationTimeUtc { get; set; }
    public DateTimeOffset? NextChangeAtUtc { get; set; }
    public string SelectionReason { get; set; } = "";
}
