using System.ComponentModel.DataAnnotations;

namespace DisputePortal.Api.Models.Requests;

public class ProfileDocumentRequest
{
    [Required]
    public string DocumentType { get; set; } = string.Empty;

    [Required]
    public string IssuerCountry { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string DocumentNumber { get; set; } = string.Empty;
    public DateOnly? DateOfBirth { get; set; }
    public int? BirthCentury { get; set; }

    [Required, MaxLength(128)]
    public string CurrentPassword { get; set; } = string.Empty;

    public ProfileDocumentRequest()
    {
    }

    public ProfileDocumentRequest(
        string DocumentType,
        string IssuerCountry,
        string DocumentNumber,
        DateOnly? DateOfBirth,
        int? BirthCentury,
        string CurrentPassword)
    {
        this.DocumentType = DocumentType;
        this.IssuerCountry = IssuerCountry;
        this.DocumentNumber = DocumentNumber;
        this.DateOfBirth = DateOfBirth;
        this.BirthCentury = BirthCentury;
        this.CurrentPassword = CurrentPassword;
    }
}
