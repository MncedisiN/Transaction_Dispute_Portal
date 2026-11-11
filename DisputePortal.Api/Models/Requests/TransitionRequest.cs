using System.ComponentModel.DataAnnotations;

namespace DisputePortal.Api.Models.Requests;

public class TransitionRequest
{
    public int ExpectedVersion { get; set; }

    [Required]
    public string Status { get; set; } = string.Empty;
    public string? Comment { get; set; }

    public TransitionRequest()
    {
    }

    public TransitionRequest(int ExpectedVersion, string Status, string? Comment)
    {
        this.ExpectedVersion = ExpectedVersion;
        this.Status = Status;
        this.Comment = Comment;
    }
}
