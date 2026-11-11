namespace DisputePortal.Api.Models.Responses;

public class CaseActionsDto
{
    public bool IsClosed { get; set; }
    public bool CanStartConversation { get; set; }
    public bool CanManage { get; set; }
    public bool CanAssign { get; set; }
    public bool CanEscalate { get; set; }
    public bool CanAddNote { get; set; }
    public bool CanWithdraw { get; set; }
    public bool CanTakeOwnership { get; set; }
    public string[] AllowedTransitions { get; set; } = [];
    public ClaimActionDto Claim { get; set; } = new();
    public AssigneeOptionDto[] Assignees { get; set; } = [];
}
