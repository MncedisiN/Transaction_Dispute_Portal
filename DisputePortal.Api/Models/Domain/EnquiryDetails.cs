using System.ComponentModel.DataAnnotations;

namespace DisputePortal.Api.Models.Domain;

public class EnquiryDetails
{
    [Required, MinLength(2), MaxLength(2000)]
    public string Description { get; set; } = null!;

    [MaxLength(1000)]
    public string? AdditionalDetails { get; set; } = null;
    public DateOnly? OccurredOn { get; set; } = null;
    public string MessageBody => Description.Trim() + (string.IsNullOrWhiteSpace(AdditionalDetails) ? "" : "\n\n" + AdditionalDetails.Trim());

    public EnquiryDetails()
    {
    }

    public EnquiryDetails(EnquiryDetails source)
    {
        Description = source.Description;
        AdditionalDetails = source.AdditionalDetails;
        OccurredOn = source.OccurredOn;
    }
}
