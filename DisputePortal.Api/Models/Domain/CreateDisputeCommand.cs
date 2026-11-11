namespace DisputePortal.Api.Models.Domain;

public class CreateDisputeCommand
{
    public Guid TransactionId { get; set; }
    public string Category { get; set; } = null!;
    public string Reason { get; set; } = null!;
    public string Description { get; set; } = null!;
    public Guid? VerificationId { get; set; } = null;
    public Guid? SessionId { get; set; } = null;
    public Guid? StaffActorId { get; set; } = null;
    public bool RequestSpecialistReview { get; set; } = false;

    public CreateDisputeCommand()
    {
    }

    public CreateDisputeCommand(CreateDisputeCommand source)
    {
        TransactionId = source.TransactionId;
        Category = source.Category;
        Reason = source.Reason;
        Description = source.Description;
        VerificationId = source.VerificationId;
        SessionId = source.SessionId;
        StaffActorId = source.StaffActorId;
        RequestSpecialistReview = source.RequestSpecialistReview;
    }
}
