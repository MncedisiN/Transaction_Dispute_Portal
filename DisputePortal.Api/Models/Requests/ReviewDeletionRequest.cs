using System.ComponentModel.DataAnnotations;

namespace DisputePortal.Api.Models.Requests;

public class ReviewDeletionRequest
{
    [Range(1, int.MaxValue)]
    public int ExpectedVersion { get; set; }

    [Required]
    public string Status { get; set; } = string.Empty;

    [Required, MinLength(10), MaxLength(500)]
    public string Comment { get; set; } = string.Empty;

    public ReviewDeletionRequest()
    {
    }

    public ReviewDeletionRequest(int ExpectedVersion, string Status, string Comment)
    {
        this.ExpectedVersion = ExpectedVersion;
        this.Status = Status;
        this.Comment = Comment;
    }
}
