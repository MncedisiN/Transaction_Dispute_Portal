namespace DisputePortal.Api.Models.Responses;

public class EvidenceRecipientDto
{
    public Guid Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;

    public EvidenceRecipientDto()
    {
    }

    public EvidenceRecipientDto(Guid Id, string DisplayName)
    {
        this.Id = Id;
        this.DisplayName = DisplayName;
    }
}
