using DisputePortal.Api.Models.Responses;
using DisputePortal.Api.Models.Domain;

namespace DisputePortal.Api.Interfaces.Repositories;

public interface ILoginSecurityRepository
{
    Task<LoginSessionGrant?> TryPasswordSession(
        Guid user,
        string expectedPasswordHash,
        string? deviceCookie,
        string userAgent,
        CancellationToken ct);

    Task<LoginFlowInfo> CreateFlow(
        Guid user,
        Guid challenge,
        string expectedPasswordHash,
        string flowSecret,
        string userAgent,
        string destination,
        bool verifyRegisteredContact,
        CancellationToken ct);

    Task<LoginResend> ReserveResend(Guid challenge, string flowSecret, string userAgent, string code, CancellationToken ct);

    Task MarkDelivered(Guid challenge, int revision, CancellationToken ct);

    Task<LoginSessionGrant> CompleteOtp(
        Guid challenge,
        string flowSecret,
        string userAgent,
        string code,
        bool remember,
        CancellationToken ct);

    Task<LoginDeviceSettingsDto> GetDevices(Guid user, string? currentCookie, CancellationToken ct);

    Task RevokeDevice(Guid user, Guid? device, CancellationToken ct);

    Task<bool?> GetLoginOtpPreference(Guid user, CancellationToken ct);

    Task SetLoginVerification(Guid user, string expectedPasswordHash, bool enabled, CancellationToken ct);
}
