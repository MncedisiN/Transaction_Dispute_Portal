namespace DisputePortal.Api.Models.Responses;

public class CaseOperationsDto
{
    public Guid CustomerId { get; set; }
    public CaseActionsDto Actions { get; set; } = new();
    public int EscalationLevel { get; set; }
    public DateTimeOffset? EscalatedAt { get; set; }
    public Guid Id { get; set; }
    public string Reference { get; set; } = "";
    public string Status { get; set; } = "";
    public string QueueType { get; set; } = "";
    public string Severity { get; set; } = "";
    public int Priority { get; set; }
    public int Version { get; set; }
    public Guid? AssignedTo { get; set; }
    public bool AutoAssignmentPaused { get; set; }
    public string? AssignedName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? AssignedAt { get; set; }
    public DateTimeOffset? ResponseDueAt { get; set; }
    public DateTimeOffset? DueAt { get; set; }
    public DateTimeOffset? FirstResponseAt { get; set; }
    public DateTimeOffset? SlaClosedAt { get; set; }
}
