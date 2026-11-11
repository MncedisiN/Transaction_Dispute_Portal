namespace DisputePortal.Api.Models.Responses;

public class AccountDeletionDto
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Reference { get; set; } = "";
    public string Status { get; set; } = "";
    public int Version { get; set; }
    public string CustomerName { get; set; } = "";
    public string? Email { get; set; }
    public string Reason { get; set; } = "";
    public string? Decision { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
}
