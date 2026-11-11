namespace DisputePortal.Api.Models.Internal;

public class AdminQueueFilter
{
    public int Page { get; set; }
    public int PageSize { get; set; }
    public string? Search { get; set; }
    public string? Status { get; set; }
    public int? Priority { get; set; }
    public DateOnly? From { get; set; } = null;
    public DateOnly? To { get; set; } = null;
}
