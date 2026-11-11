using System.Text.Json.Serialization;

namespace DisputePortal.Platform.Contracts;

public class PaymentRequest
{
    public Guid RequestId { get; set; }
    public string Kind { get; set; } = "";
    public Guid AccountId { get; set; }
    public Guid? RecipientAccountId { get; set; }

    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public decimal Amount { get; set; }
    public string? Note { get; set; } = null;
    public Guid? QuoteId { get; set; } = null;

    public PaymentRequest()
    {
    }

    public PaymentRequest(
        Guid RequestId,
        string Kind,
        Guid AccountId,
        Guid? RecipientAccountId,
        decimal Amount,
        string? Note = null,
        Guid? QuoteId = null)
    {
        this.RequestId = RequestId;
        this.Kind = Kind;
        this.AccountId = AccountId;
        this.RecipientAccountId = RecipientAccountId;
        this.Amount = Amount;
        this.Note = Note;
        this.QuoteId = QuoteId;
    }
}
