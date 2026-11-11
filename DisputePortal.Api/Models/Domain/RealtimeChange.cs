namespace DisputePortal.Api.Models.Domain;

public class RealtimeChange
{
    public IReadOnlyList<string> Topics { get; set; }
    public Guid? ResourceId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }

    public RealtimeChange(IReadOnlyList<string> topics, Guid? resourceId, DateTimeOffset occurredAt)
    {
        Topics = topics;
        ResourceId = resourceId;
        OccurredAt = occurredAt;
    }
}
