namespace DisputePortal.Api.Models.Responses;

public class EvidenceAccessDto
{
    public IReadOnlyList<EvidenceGrantDto> Grants { get; set; } = [];
    public IReadOnlyList<EvidenceRecipientDto> Recipients { get; set; } = [];

    public EvidenceAccessDto()
    {
    }

    public EvidenceAccessDto(IReadOnlyList<EvidenceGrantDto> Grants, IReadOnlyList<EvidenceRecipientDto> Recipients)
    {
        this.Grants = Grants;
        this.Recipients = Recipients;
    }
}
