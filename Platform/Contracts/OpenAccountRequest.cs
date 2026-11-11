namespace DisputePortal.Platform.Contracts;

public class OpenAccountRequest
{
    public Guid RequestId { get; set; }
    public string Type { get; set; } = "";
    public string CountryCode { get; set; } = "";
    public string Currency { get; set; } = "";

    public OpenAccountRequest()
    {
    }

    public OpenAccountRequest(Guid RequestId, string Type, string CountryCode, string Currency)
    {
        this.RequestId = RequestId;
        this.Type = Type;
        this.CountryCode = CountryCode;
        this.Currency = Currency;
    }
}
