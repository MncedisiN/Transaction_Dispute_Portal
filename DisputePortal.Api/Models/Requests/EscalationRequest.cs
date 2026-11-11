using System.ComponentModel.DataAnnotations;

namespace DisputePortal.Api.Models.Requests;

public class EscalationRequest
{
    [Range(1, int.MaxValue)]
    public int ExpectedVersion { get; set; }

    [Required, MinLength(10), MaxLength(500)]
    public string Reason { get; set; } = string.Empty;

    public EscalationRequest()
    {
    }

    public EscalationRequest(int ExpectedVersion, string Reason)
    {
        this.ExpectedVersion = ExpectedVersion;
        this.Reason = Reason;
    }
}
