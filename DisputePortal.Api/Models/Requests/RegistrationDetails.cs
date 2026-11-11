using System.ComponentModel.DataAnnotations;

namespace DisputePortal.Api.Models.Requests;

public class RegistrationDetails
{
    [Required]
    public string CountryCode { get; set; } = string.Empty;

    [Required]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string Phone { get; set; } = string.Empty;

    [Required]
    public string PhoneCountry { get; set; } = string.Empty;

    [Required]
    public string DocumentType { get; set; } = string.Empty;

    [Required]
    public string DocumentIssuer { get; set; } = string.Empty;

    [Required]
    public string DocumentNumber { get; set; } = string.Empty;
    public ResidentialAddress? Address { get; set; }
    public string AccountCurrency { get; set; } = string.Empty;
    public int? BirthCentury { get; set; }
    public DateOnly? DateOfBirth { get; set; }

    public RegistrationDetails()
    {
    }

    public RegistrationDetails(
        string CountryCode,
        string Email,
        string Phone,
        string PhoneCountry,
        string DocumentType,
        string DocumentIssuer,
        string DocumentNumber,
        ResidentialAddress? Address,
        string AccountCurrency = "",
        int? BirthCentury = null)
    {
        this.CountryCode = CountryCode;
        this.Email = Email;
        this.Phone = Phone;
        this.PhoneCountry = PhoneCountry;
        this.DocumentType = DocumentType;
        this.DocumentIssuer = DocumentIssuer;
        this.DocumentNumber = DocumentNumber;
        this.Address = Address;
        this.AccountCurrency = AccountCurrency;
        this.BirthCentury = BirthCentury;
    }

    public RegistrationDetails(RegistrationDetails other)
    {
        CountryCode = other.CountryCode;
        Email = other.Email;
        Phone = other.Phone;
        PhoneCountry = other.PhoneCountry;
        DocumentType = other.DocumentType;
        DocumentIssuer = other.DocumentIssuer;
        DocumentNumber = other.DocumentNumber;
        Address = other.Address;
        AccountCurrency = other.AccountCurrency;
        BirthCentury = other.BirthCentury;
        DateOfBirth = other.DateOfBirth;
    }
}
