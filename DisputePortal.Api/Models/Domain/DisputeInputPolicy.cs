namespace DisputePortal.Api.Models.Domain; 
public class DisputeInputPolicy
{
    public int ReasonMaximumLength { get; set; }
    public int DescriptionMinimumLength { get; set; }
    public int DescriptionMaximumLength { get; set; }
    public int CommentMaximumLength { get; set; }
    public int NoteMinimumLength { get; set; }
    public int NoteMaximumLength { get; set; }
    public int StatusMaximumLength { get; set; }
    public int IdempotencyKeyMinimumLength { get; set; }
    public int IdempotencyKeyMaximumLength { get; set; }
    public IReadOnlyList<DisputeCategory> Categories { get; set; } = null!;
}
