using System.ComponentModel.DataAnnotations;

namespace DisputePortal.Api.Models.Requests;

public class AddNoteRequest
{
    [Required]
    public string Content { get; set; } = string.Empty;

    [Required]
    public string Visibility { get; set; } = string.Empty;

    public AddNoteRequest()
    {
    }

    public AddNoteRequest(string Content, string Visibility)
    {
        this.Content = Content;
        this.Visibility = Visibility;
    }
}
