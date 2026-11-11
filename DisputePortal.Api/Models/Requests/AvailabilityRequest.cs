using System.ComponentModel.DataAnnotations;

namespace DisputePortal.Api.Models.Requests;

public class AvailabilityRequest
{
    [Required]
    public string Availability { get; set; } = string.Empty;

    public AvailabilityRequest()
    {
    }

    public AvailabilityRequest(string Availability)
    {
        this.Availability = Availability;
    }
}
