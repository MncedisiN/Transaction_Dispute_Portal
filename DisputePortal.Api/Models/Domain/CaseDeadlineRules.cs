namespace DisputePortal.Api.Models.Domain;

public static class CaseDeadlineRules
{
    public static string? ValidateCaseDeadlines(
        DateTimeOffset createdAt,
        DateTimeOffset? firstResponseAt,
        DateTimeOffset? previousResponseDueAt,
        DateTimeOffset responseDueAt,
        DateTimeOffset dueAt,
        DateTimeOffset now)
    {
        if (dueAt <= now || dueAt > now.AddYears(1))
        {
            return "Choose a resolution target in the future and within one year.";
        }

        if (responseDueAt < createdAt || (firstResponseAt is null && responseDueAt > dueAt))
        {
            return "The response target must fall between submission and the resolution target.";
        }

        if (firstResponseAt is null && responseDueAt <= now)
        {
            return "Choose a future first-response target.";
        }

        if (firstResponseAt is not null && responseDueAt != previousResponseDueAt)
        {
            return "The first-response target is locked after a response has been recorded.";
        }

        return null;
    }
}
