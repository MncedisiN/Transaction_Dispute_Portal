using DisputePortal.Api.Models.Requests;

namespace DisputePortal.Api.Models.Responses;

public class AssistedCustomerDto
{
    public Guid Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? FirstName { get; set; }
    public string? MiddleName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? IdentityNumber { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public ResidentialAddress? Address { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset RegisteredAt { get; set; }

    public AssistedCustomerDto()
    {
    }

    public AssistedCustomerDto(
        Guid Id,
        string DisplayName,
        string? FirstName,
        string? MiddleName,
        string? LastName,
        string? Email,
        string? Phone,
        string? IdentityNumber,
        DateTime? DateOfBirth,
        ResidentialAddress? Address,
        string Status,
        DateTimeOffset RegisteredAt)
    {
        this.Id = Id;
        this.DisplayName = DisplayName;
        this.FirstName = FirstName;
        this.MiddleName = MiddleName;
        this.LastName = LastName;
        this.Email = Email;
        this.Phone = Phone;
        this.IdentityNumber = IdentityNumber;
        this.DateOfBirth = DateOfBirth;
        this.Address = Address;
        this.Status = Status;
        this.RegisteredAt = RegisteredAt;
    }
}
