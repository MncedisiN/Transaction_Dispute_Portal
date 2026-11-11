using System.ComponentModel.DataAnnotations;

namespace DisputePortal.Api.Models.Requests;

public class ProfileDetailsRequest
{
    [Required, MaxLength(100)]
    public string FirstName { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string LastName { get; set; } = string.Empty;
    public ResidentialAddress? Address { get; set; }

    [MaxLength(100)]
    public string? MiddleName { get; set; }

    public ProfileDetailsRequest()
    {
    }

    public ProfileDetailsRequest(string FirstName, string LastName, ResidentialAddress? Address, string? MiddleName = null)
    {
        this.FirstName = FirstName;
        this.LastName = LastName;
        this.Address = Address;
        this.MiddleName = MiddleName;
    }
}
