namespace DisputePortal.Api.Models.Responses;

public class DisputeNoteDto
{
    public string AuthorName { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Visibility { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public ActionActorDto? ActorIdentity { get; set; }

    public DisputeNoteDto()
    {
    }

    public DisputeNoteDto(
        string AuthorName,
        string Content,
        string Visibility,
        DateTimeOffset CreatedAt,
        ActionActorDto? ActorIdentity = null)
    {
        this.AuthorName = AuthorName;
        this.Content = Content;
        this.Visibility = Visibility;
        this.CreatedAt = CreatedAt;
        this.ActorIdentity = ActorIdentity;
    }
}
