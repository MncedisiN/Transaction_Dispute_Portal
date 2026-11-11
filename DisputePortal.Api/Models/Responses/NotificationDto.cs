namespace DisputePortal.Api.Models.Responses;

public class NotificationDto
{
    public Guid Id { get; set; }
    public Guid? DisputeId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public bool IsRead { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string? Url { get; set; }

    public NotificationDto()
    {
    }

    public NotificationDto(
        Guid Id,
        Guid? DisputeId,
        string Type,
        string Title,
        string Body,
        bool IsRead,
        DateTimeOffset CreatedAt,
        string? Url = null)
    {
        this.Id = Id;
        this.DisputeId = DisputeId;
        this.Type = Type;
        this.Title = Title;
        this.Body = Body;
        this.IsRead = IsRead;
        this.CreatedAt = CreatedAt;
        this.Url = Url;
    }
}
