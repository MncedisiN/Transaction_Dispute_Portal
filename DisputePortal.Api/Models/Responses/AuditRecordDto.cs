namespace DisputePortal.Api.Models.Responses;

public class AuditRecordDto
{
    public Guid Id { get; set; }
    public string ActorName { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public Guid? EntityId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public string SafeMetadata { get; set; } = string.Empty;
    public string Outcome { get; set; } = "Success";
    public ActionActorDto? ActorIdentity { get; set; }
    public string? Source { get; set; }

    public AuditRecordDto()
    {
    }

    public AuditRecordDto(
        Guid Id,
        string ActorName,
        string Action,
        string EntityType,
        Guid? EntityId,
        DateTimeOffset OccurredAt,
        string CorrelationId,
        string SafeMetadata,
        string Outcome = "Success",
        ActionActorDto? ActorIdentity = null,
        string? Source = null)
    {
        this.Id = Id;
        this.ActorName = ActorName;
        this.Action = Action;
        this.EntityType = EntityType;
        this.EntityId = EntityId;
        this.OccurredAt = OccurredAt;
        this.CorrelationId = CorrelationId;
        this.SafeMetadata = SafeMetadata;
        this.Outcome = Outcome;
        this.ActorIdentity = ActorIdentity;
        this.Source = Source;
    }
}
