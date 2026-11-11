namespace DisputePortal.Api.Models.Domain;

public class EnquirySnapshot
{
    public string ConcernCode { get; set; } = null!;
    public string ConcernLabel { get; set; } = null!;
    public string Description { get; set; } = null!;
    public string? AdditionalDetails { get; set; }
    public DateOnly? OccurredOn { get; set; }
}
