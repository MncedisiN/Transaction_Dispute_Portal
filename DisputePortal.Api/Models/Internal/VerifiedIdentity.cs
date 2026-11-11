namespace DisputePortal.Api.Models.Internal;

public class VerifiedIdentity
{
    public Guid Id { get; set; }
    public string LoginIdentifier { get; set; } = "";
    public string Email { get; set; } = "";
    public string? Phone { get; set; }
    public bool EmailVerified { get; set; }
    public bool PhoneVerified { get; set; }
    public bool RequiresSecondFactor { get; set; }
    public string PreferredChannel { get; set; } = "email";

    public (string Channel, string Destination)? VerifiedContact()
    {
        if (PreferredChannel == "email" && EmailVerified && !string.IsNullOrWhiteSpace(Email))
        {
            return ("email", Email);
        }

        if (PreferredChannel is "sms" or "whatsapp" && PhoneVerified && !string.IsNullOrWhiteSpace(Phone))
        {
            return (PreferredChannel, Phone);
        }

        if (EmailVerified && !string.IsNullOrWhiteSpace(Email))
        {
            return ("email", Email);
        }

        if (PhoneVerified && !string.IsNullOrWhiteSpace(Phone))
        {
            return ("sms", Phone);
        }

        return null;
    }
}
