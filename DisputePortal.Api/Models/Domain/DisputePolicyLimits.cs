namespace DisputePortal.Api.Models.Domain; 
public static class DisputePolicyLimits
{
    public const decimal MaximumMoney = 1_000_000_000_000m;
    public const int MaximumDays = 36_500;
    public const int MaximumHours = MaximumDays * 24;

    public static bool ValidMoney(decimal value) => value > 0 && value <= MaximumMoney && decimal.Round(value, 2) == value;
}
