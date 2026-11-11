namespace DisputePortal.Api.Models.Responses;

public class DashboardDto
{
    public int Submitted { get; set; }
    public int UnderReview { get; set; }
    public int AwaitingCustomer { get; set; }
    public int ResolvedToday { get; set; }
    public int HighPriority { get; set; }

    public DashboardDto()
    {
    }

    public DashboardDto(int Submitted, int UnderReview, int AwaitingCustomer, int ResolvedToday, int HighPriority)
    {
        this.Submitted = Submitted;
        this.UnderReview = UnderReview;
        this.AwaitingCustomer = AwaitingCustomer;
        this.ResolvedToday = ResolvedToday;
        this.HighPriority = HighPriority;
    }
}
