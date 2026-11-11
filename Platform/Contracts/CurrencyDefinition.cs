namespace DisputePortal.Platform.Contracts;

public class CurrencyDefinition
{
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string Symbol { get; set; } = null!;
    public int Decimals { get; set; }
}
