namespace DisputePortal.Platform.Contracts;

public class UserRegisteredEvent
{
    public Guid UserId { get; set; }
    public string CountryCode { get; set; } = null!;
    public string AccountCurrency { get; set; } = null!;
}
