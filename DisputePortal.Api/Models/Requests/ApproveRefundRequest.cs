using System.ComponentModel.DataAnnotations;

namespace DisputePortal.Api.Models.Requests;

public class ApproveRefundRequest
{
    public int ExpectedVersion { get; set; }
    public Guid RequestId { get; set; }

    [Required]
    public string Reason { get; set; } = string.Empty;

    public ApproveRefundRequest()
    {
    }

    public ApproveRefundRequest(int ExpectedVersion, Guid RequestId, string Reason)
    {
        this.ExpectedVersion = ExpectedVersion;
        this.RequestId = RequestId;
        this.Reason = Reason;
    }

    public ApproveRefundRequest(ApproveRefundRequest other)
    {
        ExpectedVersion = other.ExpectedVersion;
        RequestId = other.RequestId;
        Reason = other.Reason;
    }
}
