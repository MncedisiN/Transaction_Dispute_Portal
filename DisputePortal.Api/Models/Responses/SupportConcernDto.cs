namespace DisputePortal.Api.Models.Responses;

public class SupportConcernDto
{
    public Guid PublicId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public bool RequiresCase { get; set; }
    public string ContextKind { get; set; } = "None";
    public bool CollectDate { get; set; }

    public SupportConcernDto()
    {
    }

    public SupportConcernDto(
        Guid PublicId,
        string Code,
        string Label,
        bool RequiresCase,
        string ContextKind = "None",
        bool CollectDate = false)
    {
        this.PublicId = PublicId;
        this.Code = Code;
        this.Label = Label;
        this.RequiresCase = RequiresCase;
        this.ContextKind = ContextKind;
        this.CollectDate = CollectDate;
    }
}
