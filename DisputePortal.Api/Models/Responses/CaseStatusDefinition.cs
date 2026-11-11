namespace DisputePortal.Api.Models.Responses;

public class CaseStatusDefinition
{
    public string Code { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public int SortOrder { get; set; }
    public string CustomerLabel { get; set; } = "";
    public string Tone { get; set; } = "";
    public int Stage { get; set; }
    public string NextStep { get; set; } = "";
    public string OwnerLabel { get; set; } = "";
    public string[] Steps { get; set; } = [];

    [System.Text.Json.Serialization.JsonIgnore]
    public string StepsJson { get; set; } = "";
    public long Revision { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public bool Closed { get; set; }
    public string[] WorkflowTargets { get; set; } = [];
}
