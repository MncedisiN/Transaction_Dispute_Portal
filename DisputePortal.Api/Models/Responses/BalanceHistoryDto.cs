namespace DisputePortal.Api.Models.Responses;

public class BalanceHistoryDto
{
    public long EntryId { get; set; }
    public Guid TransactionId { get; set; }
    public decimal CurrentDelta { get; set; }
    public decimal ReservedDelta { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public string Description { get; set; } = string.Empty;

    public BalanceHistoryDto()
    {
    }

    public BalanceHistoryDto(
        long EntryId,
        Guid TransactionId,
        decimal CurrentDelta,
        decimal ReservedDelta,
        DateTimeOffset RecordedAt,
        string Description)
    {
        this.EntryId = EntryId;
        this.TransactionId = TransactionId;
        this.CurrentDelta = CurrentDelta;
        this.ReservedDelta = ReservedDelta;
        this.RecordedAt = RecordedAt;
        this.Description = Description;
    }
}
