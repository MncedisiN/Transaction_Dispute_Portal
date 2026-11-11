namespace DisputePortal.Api.Models.Responses;

public class EvidenceOriginal
{
    public string FileName { get; set; } = string.Empty;
    public byte[] Content { get; set; } = [];

    public EvidenceOriginal()
    {
    }

    public EvidenceOriginal(string FileName, byte[] Content)
    {
        this.FileName = FileName;
        this.Content = Content;
    }
}
