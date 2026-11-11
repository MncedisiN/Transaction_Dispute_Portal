namespace DisputePortal.Api.Models.Domain;

public class RenderedMessage
{
    public string Subject { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;

    public RenderedMessage()
    {
    }

    public RenderedMessage(string Subject, string Action, string Text)
    {
        this.Subject = Subject;
        this.Action = Action;
        this.Text = Text;
    }
}
