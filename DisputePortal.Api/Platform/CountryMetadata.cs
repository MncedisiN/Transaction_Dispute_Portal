namespace DisputePortal.Api.Platform;

public class CountryMetadata
{
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string DialCode { get; set; } = null!;
    public string Locale { get; set; } = null!;
    public string Currency { get; set; } = null!;
    public string CurrencyName { get; set; } = null!;
    public string CurrencySymbol { get; set; } = null!;
    public int Decimals { get; set; }
}
