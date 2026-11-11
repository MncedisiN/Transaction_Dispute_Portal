using System.ComponentModel.DataAnnotations;

namespace DisputePortal.Api.Models.Requests;

public class ReinstateDisputeRequest
{
    public int ExpectedVersion { get; set; }
    public Guid RequestId { get; set; }

    [Required]
    public string Reason { get; set; } = string.Empty;

    public ReinstateDisputeRequest()
    {
    }

    public ReinstateDisputeRequest(int ExpectedVersion, Guid RequestId, string Reason)
    {
        this.ExpectedVersion = ExpectedVersion;
        this.RequestId = RequestId;
        this.Reason = Reason;
    }

    public ReinstateDisputeRequest(ReinstateDisputeRequest other)
    {
        ExpectedVersion = other.ExpectedVersion;
        RequestId = other.RequestId;
        Reason = other.Reason;
    }
}
