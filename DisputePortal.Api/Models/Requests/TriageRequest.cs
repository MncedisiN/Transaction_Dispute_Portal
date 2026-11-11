using System.ComponentModel.DataAnnotations;

namespace DisputePortal.Api.Models.Requests;

public class TriageRequest
{
    [Range(1, int.MaxValue)]
    public int ExpectedVersion { get; set; }

    [Range(1, 5)]
    public int Priority { get; set; }

    [Required]
    public string Severity { get; set; } = string.Empty;

    [Required]
    public string QueueType { get; set; } = string.Empty;

    [Required, MinLength(5), MaxLength(500)]
    public string Reason { get; set; } = string.Empty;

    public TriageRequest()
    {
    }

    public TriageRequest(int ExpectedVersion, int Priority, string Severity, string QueueType, string Reason)
    {
        this.ExpectedVersion = ExpectedVersion;
        this.Priority = Priority;
        this.Severity = Severity;
        this.QueueType = QueueType;
        this.Reason = Reason;
    }
}
