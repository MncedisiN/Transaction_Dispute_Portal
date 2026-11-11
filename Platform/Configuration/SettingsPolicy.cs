using System.Text.RegularExpressions;
using DisputePortal.Platform.Contracts;
using DisputePortal.Platform.Infrastructure.Models;

namespace DisputePortal.Platform.Configuration;

public static class SettingsPolicy
{
    public static void ValidatePlatformSettings(PlatformSettings value)
    {
        if (value is null || value.Registration is null)
        {
            throw new PlatformException(400, "invalid_settings", "Registration requirements are required.");
        }

        void Require(bool valid, string text)
        {
            if (!valid)
            {
                throw new PlatformException(400, "invalid_settings", text);
            }
        }

        Require(value.QuoteSeconds is >= 30 and <= 1800, "Set quote validity between 30 and 1,800 seconds.");
        Require(
            value.AccountTypes is { Length: > 0 and <= 20 }
                && value.AccountTypes.All(t => t.Length is > 0 and <= 40)
                && value.AccountTypes.Distinct().Count() == value.AccountTypes.Length
                && value.MaximumAccounts is >= 1 and <= 100
                && value.AccountNumberDigits is >= 8 and <= 24
                && value.ReservationSeconds is >= 30 and <= 3600,
            "Invalid account settings.");
        Require(
            value.Countries.Length > 0
                && value.Countries.Length <= 300
                && value.Currencies.Length > 0
                && value.Currencies.Length <= 300,
            "Configure supported countries and currencies.");
        Require(
            value.Currencies.Select(c => c.Code).Distinct().Count() == value.Currencies.Length
                && value.Countries.Select(c => c.Code).Distinct().Count() == value.Countries.Length,
            "Country and currency codes must be unique.");
        foreach (var currency in value.Currencies)
            Require(
            Regex.IsMatch(currency.Code, "^[A-Z]{3}$")
                && currency.Decimals is >= 0 and <= 8
                && currency.Name.Length is > 0 and <= 100
                && currency.Symbol.Length is > 0 and <= 10,
            "Invalid currency settings.");
        foreach (var country in value.Countries)
        {
            Require(
                Regex.IsMatch(country.Code, "^[A-Z]{2}$")
                    && country.Name.Length is > 0 and <= 100
                    && Regex.IsMatch(country.DialCode, "^\\+[1-9][0-9]{0,3}$")
                    && country.Currencies.Length == 1
                    && country.Currencies[0] == country.DefaultCurrency
                    && value.Currencies.Any(currency => currency.Code == country.DefaultCurrency),
                "Invalid country settings.");
            try
            {
                _ = System.Globalization.CultureInfo.GetCultureInfo(country.Locale);
                _ = TimeZoneInfo.FindSystemTimeZoneById(country.TimeZone);
            }
            catch
            {
                throw new PlatformException(400, "invalid_locale", "Use a supported locale and time zone.");
            }

            Require(
                country.Documents.Length > 0 && country.Documents.Length <= 20 && country.Regions.Length <= 1000,
                "Configure bounded identity and geographic definitions.");
            foreach (var doc in country.Documents)
            {
                Require(
                    doc.Key.Length is > 0 and <= 50
                        && doc.Label.Length is > 0 and <= 100
                        && doc.Pattern.Length <= 200
                        && (doc.Validator == "pattern"
                        || doc.Validator == "south-african-id"
                        && country.Code == "ZA"),
                    "Invalid document configuration.");
                try
                {
                    _ = new Regex(doc.Pattern, RegexOptions.NonBacktracking, TimeSpan.FromMilliseconds(50));
                }
                catch
                {
                    throw new PlatformException(400, "invalid_pattern", "Use a supported bounded document pattern.");
                }
            }
        }

        Require(value.Countries.Any(c => c.Code == value.OperatingCountry && c.Active), "Select an active operating country.");
        foreach (var rule in value.FinancialRules)
            Require(
            value.Countries.Any(country => country.Code == rule.Country)
                && value.Currencies.Any(currency => currency.Code == rule.Currency)
                && rule.Minimum > 0
                && rule.Maximum >= rule.Minimum
                && rule.Maximum <= 10000000000000000m
                && rule.DailyLimit >= rule.Maximum
                && rule.DailyLimit <= 1000000000000000000m
                && rule.FixedFee >= 0
                && rule.FixedFee <= rule.Maximum
                && rule.PercentageFee is >= 0 and <= 100,
            "Invalid financial limit or fee configuration.");
        Require(
            value.Countries.Where(country => country.Active).All(country => value.FinancialRules.Count(rule => rule.Country == country.Code && rule.Currency == country.DefaultCurrency) == 1),
            "Every active country/currency needs one financial policy.");
        Require(
            !string.IsNullOrWhiteSpace(value.Branding.Name)
                && value.Branding.Name.Length <= 80
                && Regex.IsMatch(value.Branding.LogoColor, "^#[A-Fa-f0-9]{6}$")
                && Regex.IsMatch(value.Branding.AccentColor, "^#[A-Fa-f0-9]{6}$"),
            "Invalid branding settings.");
        Require(
            value.Registration.RequireEmail
                && value.Registration.RequirePhone
                && value.Registration.RequireDocument
                && value.Registration.RequireVerification,
            "Contact verification is required before activating a financial account.");
        Require(!value.Registration.RequireAddress, "Residential addresses are optional during registration.");
    }
}
