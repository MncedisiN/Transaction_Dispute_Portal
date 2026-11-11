namespace DisputePortal.Api.Models.Domain;

public class NoteCommand
{
    public string Content { get; set; }
    public string Visibility { get; set; }

    public NoteCommand(string content, string visibility)
    {
        Content = content;
        Visibility = visibility;
    }
}
