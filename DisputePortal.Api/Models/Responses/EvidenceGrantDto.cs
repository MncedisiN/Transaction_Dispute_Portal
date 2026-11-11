namespace DisputePortal.Api.Models.Responses;

public class EvidenceGrantDto
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string GrantedBy { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }

    public EvidenceGrantDto()
    {
    }

    public EvidenceGrantDto(Guid Id, Guid UserId, string DisplayName, string GrantedBy, DateTimeOffset ExpiresAt, DateTimeOffset? RevokedAt)
    {
        this.Id = Id;
        this.UserId = UserId;
        this.DisplayName = DisplayName;
        this.GrantedBy = GrantedBy;
        this.ExpiresAt = ExpiresAt;
        this.RevokedAt = RevokedAt;
    }
}
