namespace DisputePortal.Platform.Contracts;

public class PlatformSettings
{
    public int Version { get; set; }
    public string OperatingCountry { get; set; } = null!;
    public BrandingDefinition Branding { get; set; } = null!;
    public RegistrationRequirements Registration { get; set; } = null!;
    public CurrencyDefinition[] Currencies { get; set; } = null!;
    public CountryDefinition[] Countries { get; set; } = null!;
    public FinancialRule[] FinancialRules { get; set; } = null!;
    public string[]? AccountTypes { get; set; } = null;
    public int MaximumAccounts { get; set; } = 10;
    public int AccountNumberDigits { get; set; } = 12;
    public int ReservationSeconds { get; set; } = 300;
    public int QuoteSeconds { get; set; } = 300;
    public PolicyVersionMetadata? Publication { get; set; }

    public PlatformSettings()
    {
    }

    public PlatformSettings(PlatformSettings source)
    {
        Version = source.Version;
        OperatingCountry = source.OperatingCountry;
        Branding = source.Branding;
        Registration = source.Registration;
        Currencies = source.Currencies;
        Countries = source.Countries;
        FinancialRules = source.FinancialRules;
        AccountTypes = source.AccountTypes;
        MaximumAccounts = source.MaximumAccounts;
        AccountNumberDigits = source.AccountNumberDigits;
        ReservationSeconds = source.ReservationSeconds;
        QuoteSeconds = source.QuoteSeconds;
        Publication = source.Publication;
    }
}
