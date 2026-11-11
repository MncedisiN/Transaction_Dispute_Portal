using System.Text.Json.Serialization;

namespace DisputePortal.Platform.Contracts;

public class PaymentQuoteRequest
{
    public string Kind { get; set; } = "";
    public Guid AccountId { get; set; }
    public Guid? RecipientAccountId { get; set; }

    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public decimal Amount { get; set; }

    public PaymentQuoteRequest()
    {
    }

    public PaymentQuoteRequest(string Kind, Guid AccountId, Guid? RecipientAccountId, decimal Amount)
    {
        this.Kind = Kind;
        this.AccountId = AccountId;
        this.RecipientAccountId = RecipientAccountId;
        this.Amount = Amount;
    }
}
