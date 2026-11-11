namespace DisputePortal.Platform.Infrastructure.Models;

public class SignedEventDelivery
{
    public byte[] Body { get; set; } = [];
    public string Signature { get; set; } = "";
}
