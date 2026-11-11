using System.ComponentModel.DataAnnotations;

namespace DisputePortal.Api.Models.Requests;

public class RecallRefundRequest
{
    public int ExpectedVersion { get; set; }
    public Guid RequestId { get; set; }

    [Required]
    public string Reason { get; set; } = string.Empty;

    public RecallRefundRequest()
    {
    }

    public RecallRefundRequest(int ExpectedVersion, Guid RequestId, string Reason)
    {
        this.ExpectedVersion = ExpectedVersion;
        this.RequestId = RequestId;
        this.Reason = Reason;
    }

    public RecallRefundRequest(RecallRefundRequest other)
    {
        ExpectedVersion = other.ExpectedVersion;
        RequestId = other.RequestId;
        Reason = other.Reason;
    }
}
