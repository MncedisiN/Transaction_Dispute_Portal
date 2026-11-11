namespace DisputePortal.Api.Models.Responses;

public class PageResult<T>
{
    public IReadOnlyList<T> Items { get; set; } = [];
    public int Page { get; set; }
    public int PageSize { get; set; }
    public long Total { get; set; }

    public PageResult()
    {
    }

    public PageResult(IReadOnlyList<T> Items, int Page, int PageSize, long Total)
    {
        this.Items = Items;
        this.Page = Page;
        this.PageSize = PageSize;
        this.Total = Total;
    }
}
