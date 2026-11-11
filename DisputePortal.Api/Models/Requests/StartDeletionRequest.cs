using System.ComponentModel.DataAnnotations;

namespace DisputePortal.Api.Models.Requests;

public class StartDeletionRequest
{
    [Required, MaxLength(128)]
    public string CurrentPassword { get; set; } = string.Empty;

    public StartDeletionRequest()
    {
    }

    public StartDeletionRequest(string CurrentPassword)
    {
        this.CurrentPassword = CurrentPassword;
    }
}
