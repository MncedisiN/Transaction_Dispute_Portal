namespace DisputePortal.Api.Models.Internal;

public class TransactionFilter
{
    public int Page { get; set; }
    public int PageSize { get; set; }
    public string? Search { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public string? Direction { get; set; }
    public string? Type { get; set; }
    public string Sort { get; set; } = null!;
    public Guid? AccountId { get; set; } = null;
}
