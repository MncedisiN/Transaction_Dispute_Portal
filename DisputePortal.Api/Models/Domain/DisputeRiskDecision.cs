namespace DisputePortal.Api.Models.Domain;

public class DisputeRiskDecision
{
    public bool Allowed { get; set; }
    public bool RequiresVerification { get; set; }
    public bool AssistedReview { get; set; }
    public int AgeDays { get; set; }
    public int PolicyVersion { get; set; }
    public string Message { get; set; } = null!;
    public Guid? PolicyVersionPublicId { get; set; }
}
