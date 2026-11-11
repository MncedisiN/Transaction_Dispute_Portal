namespace DisputePortal.Api.Models.Responses;

public class SupportMessagePageDto
{
    public IReadOnlyList<SupportMessageDto> Items { get; set; } = [];
    public bool HasMore { get; set; }
}
