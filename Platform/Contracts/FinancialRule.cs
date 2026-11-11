namespace DisputePortal.Platform.Contracts;

public class FinancialRule
{
    public string Country { get; set; } = null!;
    public string Currency { get; set; } = null!;
    public decimal Minimum { get; set; }
    public decimal Maximum { get; set; }
    public decimal DailyLimit { get; set; }
    public decimal FixedFee { get; set; }
    public decimal PercentageFee { get; set; }
    public string MinimumExact => Minimum.ToString(System.Globalization.CultureInfo.InvariantCulture);
    public string MaximumExact => Maximum.ToString(System.Globalization.CultureInfo.InvariantCulture);
    public string FixedFeeExact => FixedFee.ToString(System.Globalization.CultureInfo.InvariantCulture);
    public string PercentageFeeExact => PercentageFee.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public FinancialRule()
    {
    }

    public FinancialRule(FinancialRule source)
    {
        Country = source.Country;
        Currency = source.Currency;
        Minimum = source.Minimum;
        Maximum = source.Maximum;
        DailyLimit = source.DailyLimit;
        FixedFee = source.FixedFee;
        PercentageFee = source.PercentageFee;
    }
}
