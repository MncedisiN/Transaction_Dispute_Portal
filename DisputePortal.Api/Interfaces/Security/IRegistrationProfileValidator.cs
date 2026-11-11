using DisputePortal.Api.Models.Requests;

namespace DisputePortal.Api.Interfaces.Security;

public interface IRegistrationProfileValidator
{
    Task<RegistrationDetails> ValidateRegistrationProfile(
        RegistrationDetails? details,
        string contact,
        string channel,
        CancellationToken ct);
}
