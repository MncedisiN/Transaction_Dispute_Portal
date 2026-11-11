namespace DisputePortal.Api.Models.Responses;

public class StatusHistoryDto
{
    public string? PreviousStatus { get; set; }
    public string NewStatus { get; set; } = string.Empty;
    public string? ActorName { get; set; }
    public DateTimeOffset ChangedAt { get; set; }
    public string? Comment { get; set; }
    public ActionActorDto? ActorIdentity { get; set; }

    public StatusHistoryDto()
    {
    }

    public StatusHistoryDto(
        string? PreviousStatus,
        string NewStatus,
        string? ActorName,
        DateTimeOffset ChangedAt,
        string? Comment,
        ActionActorDto? ActorIdentity = null)
    {
        this.PreviousStatus = PreviousStatus;
        this.NewStatus = NewStatus;
        this.ActorName = ActorName;
        this.ChangedAt = ChangedAt;
        this.Comment = Comment;
        this.ActorIdentity = ActorIdentity;
    }
}
