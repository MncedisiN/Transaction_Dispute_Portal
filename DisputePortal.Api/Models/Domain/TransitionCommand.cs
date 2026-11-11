namespace DisputePortal.Api.Models.Domain;

public class TransitionCommand
{
    public int ExpectedVersion { get; set; }
    public string Status { get; set; }
    public string? Comment { get; set; }

    public TransitionCommand(int expectedVersion, string status, string? comment)
    {
        ExpectedVersion = expectedVersion;
        Status = status;
        Comment = comment;
    }
}
