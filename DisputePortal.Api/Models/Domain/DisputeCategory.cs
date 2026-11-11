namespace DisputePortal.Api.Models.Domain;

public class DisputeCategory
{
    public string Code { get; set; } = null!;
    public string Label { get; set; } = null!;
    public string Reason { get; set; } = null!;
    public string? TransactionType { get; set; }
    public int SortOrder { get; set; } 
    public IReadOnlyList<string> TransactionTypes { get; set; } = [];
}
