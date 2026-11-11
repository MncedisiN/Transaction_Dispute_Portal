namespace DisputePortal.Api.Models.Responses;

public class EvidenceDto
{
    public Guid Id { get; set; }
    public string Category { get; set; } = "Other";
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public int SizeBytes { get; set; }
    public string UploadedBy { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public string ScanStatus { get; set; } = "";
    public string? OriginalFileName { get; set; }
    public bool HasOriginal { get; set; }
    public bool CanView { get; set; }
    public bool CanManageAccess { get; set; }
    public bool CanRemove { get; set; }
    public bool CanRemoveOwn { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? DeletedById { get; set; }
    public string? DeletedBy { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public Guid UploadedById { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public Guid? RemovalAuditId { get; set; }
}
