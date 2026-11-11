namespace DisputePortal.Api.Models.Configuration;

public class EvidencePolicy
{
    public int Version { get; set; }
    public int MaximumFileBytes { get; set; }
    public int MaximumOriginalBytes { get; set; }
    public int MaximumFiles { get; set; }
    public int MaximumCaseBytes { get; set; }
    public int MaximumRequestBytes { get; set; }
    public int MaximumFileNameLength { get; set; }
    public int MinimumAccessHours { get; set; }
    public int MaximumAccessHours { get; set; }
    public int DefaultAccessHours { get; set; }
    public int AccessReasonMinimumLength { get; set; }
    public int AccessReasonMaximumLength { get; set; }
    public Dictionary<string, string> AccessDurations { get; set; } = [];
    public IReadOnlyList<EvidenceCategoryOption> Categories { get; set; } = [];
    public Dictionary<string, string> Formats { get; set; } = [];
}

public class EvidenceCategoryOption
{
    public long Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
}
