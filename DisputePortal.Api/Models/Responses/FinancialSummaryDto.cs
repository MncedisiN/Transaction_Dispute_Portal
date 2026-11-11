namespace DisputePortal.Api.Models.Responses;

public class FinancialSummaryDto
{
    public RefundDto? Refund { get; set; }
    public string? Decision { get; set; }
    public string? RuleCode { get; set; }
    public int? RuleVersion { get; set; }
    public RefundRecallDto? Recall { get; set; }
    public bool CanReinstate { get; set; }
    public bool CanRecall { get; set; }
    public Guid? ParentDisputeId { get; set; }
    public Guid? ReinstatedDisputeId { get; set; }
    public PendingFinancialDto? Pending { get; set; }
    public bool CanApproveRefund { get; set; }

    public FinancialSummaryDto()
    {
    }

    public FinancialSummaryDto(
        RefundDto? Refund,
        string? Decision,
        string? RuleCode,
        int? RuleVersion,
        RefundRecallDto? Recall = null,
        bool CanReinstate = false,
        bool CanRecall = false,
        Guid? ParentDisputeId = null,
        Guid? ReinstatedDisputeId = null,
        PendingFinancialDto? Pending = null,
        bool CanApproveRefund = false)
    {
        this.Refund = Refund;
        this.Decision = Decision;
        this.RuleCode = RuleCode;
        this.RuleVersion = RuleVersion;
        this.Recall = Recall;
        this.CanReinstate = CanReinstate;
        this.CanRecall = CanRecall;
        this.ParentDisputeId = ParentDisputeId;
        this.ReinstatedDisputeId = ReinstatedDisputeId;
        this.Pending = Pending;
        this.CanApproveRefund = CanApproveRefund;
    }
}
