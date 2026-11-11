using System.ComponentModel.DataAnnotations;

namespace DisputePortal.Api.Models.Requests;

public class UpdateCaseStatusRequest
{
    public long ExpectedRevision { get; set; }

    [Required, StringLength(120, MinimumLength = 1)]
    public string DisplayName { get; set; } = "";

    [Required, StringLength(120, MinimumLength = 1)]
    public string CustomerLabel { get; set; } = "";

    [Required, StringLength(500, MinimumLength = 1)]
    public string NextStep { get; set; } = "";

    [Range(0, 1000)]
    public int SortOrder { get; set; }

    [Required, StringLength(500, MinimumLength = 10)]
    public string Reason { get; set; } = "";
}
