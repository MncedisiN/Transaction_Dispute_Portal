using System.ComponentModel.DataAnnotations;

namespace DisputePortal.Api.Models.Requests;

public class CreateDisputeRequest
{
    public Guid TransactionId { get; set; }

    [Required]
    public string Category { get; set; } = string.Empty;

    [Required]
    public string Reason { get; set; } = string.Empty;

    [Required]
    public string Description { get; set; } = string.Empty;
    public Guid? VerificationId { get; set; }
    public bool RequestSpecialistReview { get; set; }

    public CreateDisputeRequest()
    {
    }

    public CreateDisputeRequest(
        Guid TransactionId,
        string Category,
        string Reason,
        string Description,
        Guid? VerificationId = null,
        bool RequestSpecialistReview = false)
    {
        this.TransactionId = TransactionId;
        this.Category = Category;
        this.Reason = Reason;
        this.Description = Description;
        this.VerificationId = VerificationId;
        this.RequestSpecialistReview = RequestSpecialistReview;
    }
}
