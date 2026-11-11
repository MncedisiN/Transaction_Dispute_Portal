using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using DisputePortal.Api.Models.Domain;
using DisputePortal.Api.Models.Exceptions;
using DisputePortal.Api.Models.Requests;
using DisputePortal.Platform.Contracts;
using PhoneNumbers;
using DisputePortal.Api.Interfaces.Security;
using DisputePortal.Platform.Infrastructure.Interfaces;

namespace DisputePortal.Api.Platform;

public class RegistrationProfileValidator(IServiceClient client) : IRegistrationProfileValidator
{
    public async Task<RegistrationDetails> ValidateRegistrationProfile(
        RegistrationDetails? d,
        string contact,
        string channel,
        CancellationToken ct)
    {
        if (d is null)
        {
            throw new PortalValidationException("Complete your country, contact, identity details.");
        }

        var settings = await client.SendServiceRequest<PlatformSettings>("configuration", HttpMethod.Get, "/settings", ct: ct);
        var country = settings.Countries.SingleOrDefault(c => c.Code == d.CountryCode
            && c.Active) ?? throw new PortalValidationException("Choose an available country of residence.");
        var email = d.Email.Trim();
        if (email.Length > 320 || !new EmailAddressAttribute().IsValid(email))
        {
            throw new PortalValidationException("Enter a valid email address.");
        }

        var phones = PhoneNumberUtil.GetInstance();
        string phone;
        try
        {
            var parsed = phones.Parse(d.Phone, d.PhoneCountry);
            if (!phones.GetSupportedRegions().Contains(d.PhoneCountry) || !phones.IsValidNumberForRegion(parsed, d.PhoneCountry))
            {
                throw new FormatException();
            }

            phone = phones.Format(parsed, PhoneNumberFormat.E164);
        }
        catch (Exception e) when (e is NumberParseException or FormatException)
        {
            throw new PortalValidationException("Check the mobile number and its selected country code.");
        }

        if (channel == "email"
            && !string.Equals(email, contact, StringComparison.OrdinalIgnoreCase)
            || channel != "email"
            && phone != contact)
        {
            throw new PortalValidationException("The verification contact must match your registration details.");
        }

        var issuer = settings.Countries.SingleOrDefault(c => c.Code == d.DocumentIssuer
            && c.Active) ?? throw new PortalValidationException("Choose an identity document issuer.");
        var document = issuer.Documents.SingleOrDefault(v => v.Key == d.DocumentType) ?? throw new PortalValidationException("Choose a supported identity document.");
        var number = d.DocumentNumber.Trim().ToUpperInvariant();
        if (number.Length is < 4 or > 100
            || !Regex.IsMatch(number, document.Pattern, RegexOptions.NonBacktracking, TimeSpan.FromMilliseconds(50)))
        {
            throw new PortalValidationException("Check your identity document number.");
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var dateOfBirth = d.DateOfBirth;
        if (document.Validator == "south-african-id")
        {
            if (issuer.Code != "ZA" || !SouthAfricanIdentity.TryGetBirthDate(
                number, d.BirthCentury ?? SouthAfricanIdentity.InferCentury(number, today),
                today, out var derivedBirthDate))
            {
                throw new PortalValidationException("Check your South African ID number.");
            }
            dateOfBirth = derivedBirthDate;
        }
        if (dateOfBirth is not { } birthDate || birthDate > today || birthDate.Year < 1900)
        {
            throw new PortalValidationException("Enter a valid date of birth for your identity document.");
        }

        var a = d.Address;
        if (a is not null
            && (a.CountryCode != country.Code
            || string.IsNullOrWhiteSpace(a.Line1)
            || a.Line1.Length > 200
            || string.IsNullOrWhiteSpace(a.City)
            || a.City.Length > 100
            || a.Line2?.Length > 200
            || a.Suburb?.Length > 100
            || a.Province?.Length > 100
            || a.Municipality?.Length > 100
            || a.PostalCode?.Length > 20
            || country.PostalRequired
            && string.IsNullOrWhiteSpace(a.PostalCode)
            || country.Regions.Length > 0
            && !country.Regions.Any(r => r.Code == a.Province)))
        {
            throw new PortalValidationException("Complete your address using the selected country’s regions and postal requirements.");
        }

        var operatingCountry = settings.Countries.SingleOrDefault(c => c.Code == settings.OperatingCountry
            && c.Active) ?? throw new PortalConflictException("The operating country is not available. Contact support.");
        if (!string.IsNullOrWhiteSpace(d.AccountCurrency) && d.AccountCurrency != operatingCountry.DefaultCurrency)
        {
            throw new PortalValidationException("The account currency must match the saved operating country.");
        }

        return new RegistrationDetails(d)
        {
            Email = email,
            Phone = phone,
            DocumentNumber = number,
            DateOfBirth = dateOfBirth,
            AccountCurrency = operatingCountry.DefaultCurrency
        };
    }
}
