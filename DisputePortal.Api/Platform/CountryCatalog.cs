using System.Globalization;
using PhoneNumbers;

namespace DisputePortal.Api.Platform;

public static class CountryCatalog
{
    private static readonly Lazy<CountryMetadata[]> Data = new(BuildCountryMetadata);
    public static CountryMetadata[] All => Data.Value;

    private static CountryMetadata[] BuildCountryMetadata()
    {
        var phone = PhoneNumberUtil.GetInstance();
        var supported = phone.GetSupportedRegions();
        return CultureInfo.GetCultures(CultureTypes.SpecificCultures).Select(c =>
        {
            try
            {
                return new
                {
                    Culture = c,
                    Region = new RegionInfo(c.Name)
                };
            }
            catch
            {
                return null;
            }
        }).Where(x => x is not null && supported.Contains(x.Region.TwoLetterISORegionName)).GroupBy(x => x!.Region.TwoLetterISORegionName).Select(g =>
        {
            var value = g.OrderBy(x => x!.Culture.Name.StartsWith("en-", StringComparison.OrdinalIgnoreCase) ? 0 : 1).ThenBy(x => x!.Culture.Name).First()!;
            var region = value.Region;
            return new CountryMetadata
            {
                Code = region.TwoLetterISORegionName,
                Name = region.EnglishName,
                DialCode = "+" + phone.GetCountryCodeForRegion(region.TwoLetterISORegionName),
                Locale = value.Culture.Name,
                Currency = region.ISOCurrencySymbol,
                CurrencyName = region.CurrencyEnglishName,
                CurrencySymbol = region.CurrencySymbol,
                Decimals = value.Culture.NumberFormat.CurrencyDecimalDigits
            };
        }).OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
