using DisputePortal.Api.Models.Security;

namespace DisputePortal.Api.Interfaces.Security;

public interface IVerificationDelivery
{
    bool IsAvailable(string channel);

    Task SendVerificationCode(
        string channel,
        string destination,
        string code,
        int minutes,
        VerificationPurpose purpose,
        CancellationToken cancellationToken);
}
