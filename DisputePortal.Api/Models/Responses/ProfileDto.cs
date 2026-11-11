using DisputePortal.Api.Models.Requests;

namespace DisputePortal.Api.Models.Responses;

public class ProfileDto
{
    public string? ResidenceCountry { get; set; }
    public string? IdentityDocumentType { get; set; }
    public string? IdentityIssuerCountry { get; set; }
    public Guid Id { get; set; }
    public string DisplayName { get; set; } = "";
    public string Email { get; set; } = "";
    public string? Phone { get; set; }
    public DateTimeOffset RegisteredAt { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? MiddleName { get; set; }
    public string? MaskedIdentityNumber { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public ResidentialAddress? Address { get; set; }
}
