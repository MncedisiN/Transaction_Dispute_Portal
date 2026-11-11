namespace DisputePortal.Platform.Contracts;

public class CountryDefinition
{
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string Flag { get; set; } = null!;
    public string DialCode { get; set; } = null!;
    public string Locale { get; set; } = null!;
    public string TimeZone { get; set; } = null!;
    public string DefaultCurrency { get; set; } = null!;
    public string[] Currencies { get; set; } = null!;
    public IdentityDocumentDefinition[] Documents { get; set; } = null!;
    public RegionDefinition[] Regions { get; set; } = null!;
    public string RegionLabel { get; set; } = null!;
    public string PostalLabel { get; set; } = null!;
    public bool PostalRequired { get; set; }
    public bool Active { get; set; } = true;

    public CountryDefinition()
    {
    }

    public CountryDefinition(CountryDefinition source)
    {
        Code = source.Code;
        Name = source.Name;
        Flag = source.Flag;
        DialCode = source.DialCode;
        Locale = source.Locale;
        TimeZone = source.TimeZone;
        DefaultCurrency = source.DefaultCurrency;
        Currencies = source.Currencies;
        Documents = source.Documents;
        Regions = source.Regions;
        RegionLabel = source.RegionLabel;
        PostalLabel = source.PostalLabel;
        PostalRequired = source.PostalRequired;
        Active = source.Active;
    }
}
