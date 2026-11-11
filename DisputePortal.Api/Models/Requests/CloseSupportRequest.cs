using System.ComponentModel.DataAnnotations;

namespace DisputePortal.Api.Models.Requests;

public class CloseSupportRequest
{
    [Range(1, int.MaxValue)]
    public int ExpectedVersion { get; set; }

    [MaxLength(500)]
    public string? Reason { get; set; }

    public CloseSupportRequest()
    {
    }

    public CloseSupportRequest(int ExpectedVersion, string? Reason = null)
    {
        this.ExpectedVersion = ExpectedVersion;
        this.Reason = Reason;
    }
}
