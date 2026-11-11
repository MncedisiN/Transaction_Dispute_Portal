namespace DisputePortal.Api.Models.Responses;

public class PolicyLimitsDto
{
    public decimal MaximumMoney { get; set; }
    public int MaximumDays { get; set; }
    public int MaximumHours { get; set; }

    public PolicyLimitsDto()
    {
    }

    public PolicyLimitsDto(decimal MaximumMoney, int MaximumDays, int MaximumHours)
    {
        this.MaximumMoney = MaximumMoney;
        this.MaximumDays = MaximumDays;
        this.MaximumHours = MaximumHours;
    }
}
