namespace DisputePortal.Api.Models.Responses;

public class EvidenceDownload
{
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public byte[] Content { get; set; } = [];
    public string ScanStatus { get; set; } = string.Empty;

    public EvidenceDownload()
    {
    }

    public EvidenceDownload(string FileName, string ContentType, byte[] Content, string ScanStatus)
    {
        this.FileName = FileName;
        this.ContentType = ContentType;
        this.Content = Content;
        this.ScanStatus = ScanStatus;
    }
}
