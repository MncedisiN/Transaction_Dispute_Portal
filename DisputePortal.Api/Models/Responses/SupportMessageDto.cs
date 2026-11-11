namespace DisputePortal.Api.Models.Responses;

public class SupportMessageDto
{
    [System.Text.Json.Serialization.JsonIgnore]
    public string? EnquiryJson { get; set; }
    public DisputePortal.Api.Models.Domain.EnquirySnapshot? Enquiry { get; set; }
    public string AuthorRole { get; set; } = "Customer";
    public ActionActorDto? ActorIdentity { get; set; }
    public long Sequence { get; set; }
    public Guid Id { get; set; }
    public Guid AuthorId { get; set; }
    public string AuthorName { get; set; } = "";
    public string Body { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}
