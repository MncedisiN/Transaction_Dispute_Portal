using System.Text.RegularExpressions;

namespace DisputePortal.Api.Models.Domain;

public static class ContactNumber
{
    public static bool TryNormalize(string? input, string? country, out string normalized)
    {
        if (string.IsNullOrWhiteSpace(country))
        {
            return TryNormalize(input, out normalized);
        }

        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(input)
            || input.Length > 40
            || input.Any(character => !char.IsAsciiDigit(character)
            && character is not ('+' or ' ' or '-' or '(' or ')')))
        {
            return false;
        }

        var region = country.Trim().ToUpperInvariant();
        var numbers = PhoneNumbers.PhoneNumberUtil.GetInstance();
        if (!numbers.GetSupportedRegions().Contains(region))
        {
            return false;
        }

        try
        {
            var number = numbers.Parse(input, region);
            if (!numbers.IsValidNumberForRegion(number, region))
            {
                return false;
            }

            normalized = numbers.Format(number, PhoneNumbers.PhoneNumberFormat.E164);
            return true;
        }
        catch (PhoneNumbers.NumberParseException)
        {
            return false;
        }
    }

    public static bool TryNormalize(string? input, out string normalized)
    {
        normalized = "";
        if (string.IsNullOrWhiteSpace(input) || input.Length > 40)
        {
            return false;
        }

        if (input.Any(c => !(c is >= '0' and <= '9') && c is not ('+' or ' ' or '-' or '(' or ')')))
        {
            return false;
        }

        var value = string.Concat(input.Where(c => c is not (' ' or '-' or '(' or ')')));
        if (Regex.IsMatch(value, "^0[6-8][0-9]{8}$"))
        {
            value = "+27" + value[1..];
        }
        else if (Regex.IsMatch(value, "^27[0-9]{9}$"))
        {
            value = "+" + value;
        }
        else if (value.StartsWith("00", StringComparison.Ordinal))
        {
            value = "+" + value[2..];
        }

        if (!Regex.IsMatch(value, @"^\+[1-9][0-9]{7,14}$"))
        {
            return false;
        }

        normalized = value;
        return true;
    }
}
