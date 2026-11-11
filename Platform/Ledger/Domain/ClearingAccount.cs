namespace DisputePortal.Platform.Ledger.Domain;

public class ClearingAccount
{
    public Guid Id { get; set; }
    public string Currency { get; set; } = null!;
    public string Purpose { get; set; } = null!;
}
