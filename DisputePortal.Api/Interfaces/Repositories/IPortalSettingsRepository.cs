using DisputePortal.Api.Models.Requests;
using DisputePortal.Api.Models.Responses;

namespace DisputePortal.Api.Interfaces.Repositories;

public interface IPortalSettingsRepository
{
    Task<PortalSettingsVersionDto> GetPortalSettings(CancellationToken ct);
    Task<PortalSettingsVersionDto> PublishPortalSettings(Guid actor, UpdatePortalSettingsRequest request, CancellationToken ct);
}
