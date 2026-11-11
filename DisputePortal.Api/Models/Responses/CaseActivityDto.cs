namespace DisputePortal.Api.Models.Responses;

public class CaseActivityDto
{
    public string Id { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Actor { get; set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; set; }
    public string? Detail { get; set; }
    public ActionActorDto? ActorIdentity { get; set; }

    public CaseActivityDto()
    {
    }

    public CaseActivityDto(
        string Id,
        string Action,
        string Actor,
        DateTimeOffset OccurredAt,
        string? Detail,
        ActionActorDto? ActorIdentity = null)
    {
        this.Id = Id;
        this.Action = Action;
        this.Actor = Actor;
        this.OccurredAt = OccurredAt;
        this.Detail = Detail;
        this.ActorIdentity = ActorIdentity;
    }
}
