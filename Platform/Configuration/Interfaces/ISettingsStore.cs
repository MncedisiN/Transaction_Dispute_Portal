using DisputePortal.Platform.Contracts;

namespace DisputePortal.Platform.Configuration.Interfaces;

public interface ISettingsStore
{
    Task<PlatformSettings> GetPlatformSettings(CancellationToken ct);

    Task<PlatformSettings> UpdatePlatformSettings(SettingsUpdate request, Guid? actor, CancellationToken ct);
}
