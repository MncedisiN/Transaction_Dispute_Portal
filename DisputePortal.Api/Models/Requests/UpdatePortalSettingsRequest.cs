using System.ComponentModel.DataAnnotations;
using DisputePortal.Api.Models.Configuration;

namespace DisputePortal.Api.Models.Requests;

public class UpdatePortalSettingsRequest
{
    [Range(1, int.MaxValue)]
    public int ExpectedVersion { get; set; }

    [Required]
    public PortalReferenceSettings? Settings { get; set; }

    [Required, StringLength(500, MinimumLength = 5)]
    public string Reason { get; set; } = string.Empty;
}
