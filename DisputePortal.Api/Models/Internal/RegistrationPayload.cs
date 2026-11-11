using DisputePortal.Api.Models.Requests;

namespace DisputePortal.Api.Models.Internal;

public class RegistrationPayload
{
    public string DisplayName { get; set; } = string.Empty;
    public string Contact { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public RegistrationDetails? Details { get; set; }

    public RegistrationPayload()
    {
    }

    public RegistrationPayload(
        string DisplayName,
        string Contact,
        string Channel,
        string PasswordHash,
        string? FirstName = null,
        string? LastName = null,
        RegistrationDetails? Details = null)
    {
        this.DisplayName = DisplayName;
        this.Contact = Contact;
        this.Channel = Channel;
        this.PasswordHash = PasswordHash;
        this.FirstName = FirstName;
        this.LastName = LastName;
        this.Details = Details;
    }
}
