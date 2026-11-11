namespace DisputePortal.Api.Models.Responses;

public class SupportThreadDto
{
    public string Status { get; set; } = "Open";
    public string ConcernCode { get; set; } = "General";
    public string ConcernLabel { get; set; } = "General Enquiry";
    public string? QueueType { get; set; } = "General";
    public Guid? AssignedTo { get; set; }
    public int Version { get; set; } = 1;
    public int EscalationLevel { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public bool CanReply { get; set; }
    public bool CanClose { get; set; }
    public bool CanAssign { get; set; }
    public bool CanEscalate { get; set; }
    public Guid? RelatedDisputeId { get; set; }
    public string? RelatedCaseContext { get; set; }
    public Guid? TransactionId { get; set; }
    public string? AccountNumber { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset? TransactionAt { get; set; }
    public string? CaseTitle { get; set; }
    public DateTimeOffset? CaseCreatedAt { get; set; }
    public string? CaseStatus { get; set; }
    public int UnreadCount { get; set; }
    public long LastReadSequence { get; set; }
    public DateTimeOffset LastMessageAt { get; set; }
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public Guid? DisputeId { get; set; }
    public string? Reference { get; set; }
    public string Subject { get; set; } = "";
    public string CustomerName { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}
