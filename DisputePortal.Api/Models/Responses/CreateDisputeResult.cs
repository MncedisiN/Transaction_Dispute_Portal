namespace DisputePortal.Api.Models.Responses;

public class CreateDisputeResult
{
    public Guid Id { get; set; }
    public string Reference { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool Replayed { get; set; }

    public CreateDisputeResult()
    {
    }

    public CreateDisputeResult(Guid Id, string Reference, string Status, bool Replayed = false)
    {
        this.Id = Id;
        this.Reference = Reference;
        this.Status = Status;
        this.Replayed = Replayed;
    }
}
