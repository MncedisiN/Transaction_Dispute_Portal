using DisputePortal.Api.Models.Configuration;

namespace DisputePortal.Api.Models.Responses;

public class PortalSettingsVersionDto
{
    public Guid PublicId { get; set; }
    public int Version { get; set; }
    public PortalReferenceSettings Settings { get; set; } = new();
}
