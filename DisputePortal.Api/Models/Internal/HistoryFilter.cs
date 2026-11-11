using DisputePortal.Api.Models.Exceptions;

namespace DisputePortal.Api.Models.Internal;

public class HistoryFilter
{
    public string? Status { get; set; } = null;
    public DateOnly? From { get; set; } = null;
    public DateOnly? To { get; set; } = null;
    public bool UnreadOnly { get; set; } = false;

    public void ValidateHistoryFilter()
    {
        if (Status is not (null or "" or "active" or "closed" or "Submitted" or "UnderReview" or "AwaitingCustomer" or "Resolved" or "Declined" or "Withdrawn"))
        {
            throw new PortalValidationException("Choose a valid case status.");
        }

        if (From.HasValue && To.HasValue && From > To)
        {
            throw new PortalValidationException("End date must be on or after start date.");
        }

        if (From?.Year < 1900 || To?.Year < 1900 || From?.Year > 9998 || To?.Year > 9998)
        {
            throw new PortalValidationException("Choose a supported date range.");
        }
    }

    public DateTimeOffset? StartUtc(TimeZoneInfo zone) => From is { } day ? MidnightUtc(day, zone) : null;

    public DateTimeOffset? EndUtc(TimeZoneInfo zone) => To is { } day ? MidnightUtc(day.AddDays(1), zone) : null;

    private static DateTimeOffset MidnightUtc(DateOnly day, TimeZoneInfo zone) => new(
        TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), zone),
        TimeSpan.Zero);

    public HistoryFilter()
    {
    }

    public HistoryFilter(HistoryFilter source)
    {
        Status = source.Status;
        From = source.From;
        To = source.To;
        UnreadOnly = source.UnreadOnly;
    }
}
