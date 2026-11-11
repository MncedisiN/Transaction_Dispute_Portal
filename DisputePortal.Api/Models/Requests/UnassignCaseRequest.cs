using System.ComponentModel.DataAnnotations;

namespace DisputePortal.Api.Models.Requests;

public class UnassignCaseRequest
{
    [Range(1, int.MaxValue)]
    public int ExpectedVersion { get; set; }

    [Required, MinLength(5), MaxLength(500)]
    public string Reason { get; set; } = string.Empty;

    public UnassignCaseRequest()
    {
    }

    public UnassignCaseRequest(int ExpectedVersion, string Reason)
    {
        this.ExpectedVersion = ExpectedVersion;
        this.Reason = Reason;
    }
}
