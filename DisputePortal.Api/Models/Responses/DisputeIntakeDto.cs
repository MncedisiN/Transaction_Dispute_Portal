using DisputePortal.Api.Models.Domain;

namespace DisputePortal.Api.Models.Responses;

public class DisputeIntakeDto
{
    public bool Allowed { get; set; }
    public bool RequiresVerification { get; set; }
    public bool AssistedReview { get; set; }
    public int AgeDays { get; set; }
    public int PolicyVersion { get; set; }
    public Guid? PolicyVersionPublicId { get; set; }
    public string Message { get; set; } = string.Empty;
    public bool CanStart { get; set; }
    public Guid? ActiveDisputeId { get; set; }
    public string? BlockReason { get; set; }
    public bool HasPendingFinancialOperation { get; set; }
    public bool HasPostedRefund { get; set; }

    public DisputeIntakeDto()
    {
    }

    public DisputeIntakeDto(DisputeRiskDecision decision)
    {
        Allowed = decision.Allowed;
        RequiresVerification = decision.RequiresVerification;
        AssistedReview = decision.AssistedReview;
        AgeDays = decision.AgeDays;
        PolicyVersion = decision.PolicyVersion;
        Message = decision.Message;
        CanStart = decision.Allowed || decision.AssistedReview;
        BlockReason = CanStart ? null : decision.Message;
    }
}
