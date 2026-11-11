namespace DisputePortal.Api.Models.Domain;

public static class DisputeWorkflow
{
    private static readonly IReadOnlyDictionary<DisputeStatus, HashSet<DisputeStatus>> Allowed = new Dictionary<DisputeStatus, HashSet<DisputeStatus>>
    {
        [DisputeStatus.Submitted] = [DisputeStatus.UnderReview, DisputeStatus.Withdrawn],
        [DisputeStatus.UnderReview] = [DisputeStatus.AwaitingCustomer, DisputeStatus.Resolved, DisputeStatus.Declined],
        [DisputeStatus.AwaitingCustomer] = [DisputeStatus.UnderReview, DisputeStatus.Withdrawn],
        [DisputeStatus.Resolved] = [DisputeStatus.UnderReview],
        [DisputeStatus.Declined] = [DisputeStatus.UnderReview],
        [DisputeStatus.Withdrawn] = [DisputeStatus.UnderReview]
    };

    public static bool CanTransition(DisputeStatus from, DisputeStatus to) =>
        Allowed.TryGetValue(from, out var destinations) && destinations.Contains(to);

    public static bool IsActive(DisputeStatus status) =>
        status is DisputeStatus.Submitted or DisputeStatus.UnderReview or DisputeStatus.AwaitingCustomer;

    public static bool CanCustomerWithdraw(DisputeStatus status) => IsActive(status);

    public static bool CanSendCaseMessage(string? status) =>
        status is nameof(DisputeStatus.Submitted) or nameof(DisputeStatus.UnderReview) or nameof(DisputeStatus.AwaitingCustomer);
}
