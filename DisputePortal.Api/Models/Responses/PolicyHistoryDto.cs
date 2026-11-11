using DisputePortal.Platform.Contracts;

namespace DisputePortal.Api.Models.Responses;

public class PolicyHistoryDto<T>
    where T : PolicyVersionMetadata
{
    public IReadOnlyList<T> Items { get; set; } = [];
    public long Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public long ConcurrencyToken { get; set; }
    public PolicyResolution<T> Effective { get; set; } = new();
}
