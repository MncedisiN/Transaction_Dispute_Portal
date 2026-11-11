using System.ComponentModel.DataAnnotations;

namespace DisputePortal.Api.Models.Requests;

public class ResidentialAddress
{
    [Required, MaxLength(200)]
    public string Line1 { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? Line2 { get; set; }

    [MaxLength(100)]
    public string? Suburb { get; set; }

    [Required, MaxLength(100)]
    public string City { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? Province { get; set; }

    [MaxLength(20)]
    public string? PostalCode { get; set; }

    [Required, RegularExpression("^[A-Z]{2}$")]
    public string CountryCode { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? Municipality { get; set; }

    public ResidentialAddress()
    {
    }

    public ResidentialAddress(
        string Line1,
        string? Line2,
        string? Suburb,
        string City,
        string? Province,
        string? PostalCode,
        string CountryCode,
        string? Municipality = null)
    {
        this.Line1 = Line1;
        this.Line2 = Line2;
        this.Suburb = Suburb;
        this.City = City;
        this.Province = Province;
        this.PostalCode = PostalCode;
        this.CountryCode = CountryCode;
        this.Municipality = Municipality;
    }

    public ResidentialAddress(ResidentialAddress other)
    {
        Line1 = other.Line1;
        Line2 = other.Line2;
        Suburb = other.Suburb;
        City = other.City;
        Province = other.Province;
        PostalCode = other.PostalCode;
        CountryCode = other.CountryCode;
        Municipality = other.Municipality;
    }
}
