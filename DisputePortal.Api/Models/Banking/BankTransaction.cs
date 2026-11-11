namespace DisputePortal.Api.Models.Banking;

public class BankTransaction
{
    public string ExternalId { get; set; } = null!;
    public DateTimeOffset OccurredAt { get; set; }
    public string Description { get; set; } = null!;
    public string MerchantName { get; set; } = null!;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = null!;
    public string Type { get; set; } = null!;
    public string Direction { get; set; } = null!;
    public string Status { get; set; } = null!;
    public bool DisputeEligible { get; set; }
    public string? DebitOrderScheme { get; set; } = null;
    public string? MandateStatus { get; set; } = null;

    public BankTransaction()
    {
    }

    public BankTransaction(BankTransaction source)
    {
        ExternalId = source.ExternalId;
        OccurredAt = source.OccurredAt;
        Description = source.Description;
        MerchantName = source.MerchantName;
        Amount = source.Amount;
        Currency = source.Currency;
        Type = source.Type;
        Direction = source.Direction;
        Status = source.Status;
        DisputeEligible = source.DisputeEligible;
        DebitOrderScheme = source.DebitOrderScheme;
        MandateStatus = source.MandateStatus;
    }
}
