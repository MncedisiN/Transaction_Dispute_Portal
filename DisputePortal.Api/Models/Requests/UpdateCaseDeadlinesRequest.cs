using System.ComponentModel.DataAnnotations;

namespace DisputePortal.Api.Models.Requests;

public class UpdateCaseDeadlinesRequest
{
    [Range(1, int.MaxValue)]
    public int ExpectedVersion { get; set; }
    public DateTimeOffset ResponseDueAt { get; set; }
    public DateTimeOffset DueAt { get; set; }

    [Required, MinLength(5), MaxLength(500)]
    public string Reason { get; set; } = string.Empty;

    public UpdateCaseDeadlinesRequest()
    {
    }

    public UpdateCaseDeadlinesRequest(int ExpectedVersion, DateTimeOffset ResponseDueAt, DateTimeOffset DueAt, string Reason)
    {
        this.ExpectedVersion = ExpectedVersion;
        this.ResponseDueAt = ResponseDueAt;
        this.DueAt = DueAt;
        this.Reason = Reason;
    }

    public UpdateCaseDeadlinesRequest(UpdateCaseDeadlinesRequest other)
    {
        ExpectedVersion = other.ExpectedVersion;
        ResponseDueAt = other.ResponseDueAt;
        DueAt = other.DueAt;
        Reason = other.Reason;
    }
}
