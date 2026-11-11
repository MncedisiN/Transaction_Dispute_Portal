namespace DisputePortal.Platform.Contracts;

public static class CountryCurrencyPolicy
{
    public static CountryDefinition Current(PlatformSettings settings) =>
        settings.Countries.SingleOrDefault(country => country.Code == settings.OperatingCountry
        && country.Active) ?? throw new InvalidOperationException("The saved operating country is unavailable.");

    public static bool IsCurrent(PlatformSettings settings, string countryCode, string currency)
    {
        var country = settings.Countries.SingleOrDefault(country => country.Code == settings.OperatingCountry && country.Active);
        return country is not null
            && country.Code == countryCode
            && country.DefaultCurrency == currency
            && settings.Currencies.Any(definition => definition.Code == currency);
    }
}
