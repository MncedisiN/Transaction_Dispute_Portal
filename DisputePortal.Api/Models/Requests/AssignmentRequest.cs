using System.ComponentModel.DataAnnotations;

namespace DisputePortal.Api.Models.Requests;

public class AssignmentRequest
{
    [Range(1, int.MaxValue)]
    public int ExpectedVersion { get; set; }
    public Guid? UserId { get; set; }

    [Required, MinLength(5), MaxLength(500)]
    public string Reason { get; set; } = string.Empty;

    public AssignmentRequest()
    {
    }

    public AssignmentRequest(int ExpectedVersion, Guid? UserId, string Reason)
    {
        this.ExpectedVersion = ExpectedVersion;
        this.UserId = UserId;
        this.Reason = Reason;
    }

    public AssignmentRequest(AssignmentRequest other)
    {
        ExpectedVersion = other.ExpectedVersion;
        UserId = other.UserId;
        Reason = other.Reason;
    }
}
