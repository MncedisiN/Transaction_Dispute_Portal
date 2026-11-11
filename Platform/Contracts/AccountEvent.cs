namespace DisputePortal.Platform.Contracts;

public class AccountEvent
{
    public Guid AccountId { get; set; }
    public Guid UserId { get; set; }
    public string CountryCode { get; set; } = null!;
    public string Currency { get; set; } = null!;
    public string AccountType { get; set; } = null!;
}
