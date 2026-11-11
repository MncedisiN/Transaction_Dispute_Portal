namespace DisputePortal.Platform.Contracts;

public class ScheduleReversal
{
    public Guid RequestId { get; set; }
    public Guid OriginalTransactionId { get; set; }
    public string Kind { get; set; } = "";
    public DateTimeOffset DueAt { get; set; }
    public string Reason { get; set; } = "";

    public ScheduleReversal()
    {
    }

    public ScheduleReversal(Guid RequestId, Guid OriginalTransactionId, string Kind, DateTimeOffset DueAt, string Reason)
    {
        this.RequestId = RequestId;
        this.OriginalTransactionId = OriginalTransactionId;
        this.Kind = Kind;
        this.DueAt = DueAt;
        this.Reason = Reason;
    }
}
