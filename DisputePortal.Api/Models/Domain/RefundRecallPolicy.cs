namespace DisputePortal.Api.Models.Domain;

public static class RefundRecallPolicy
{
    public const int ReasonMinimumLength = 10;
    public const int ReasonMaximumLength = 1000;

    public static bool HasAvailableFunds(decimal amount, decimal current, decimal reserved, bool known) =>
        known && amount > 0 && current - reserved >= amount;

    public static bool CanReinstate(string status, bool recalled, bool alreadyReinstated) =>
        CaseServicePolicy.IsClosed(status) && recalled && !alreadyReinstated;
}
