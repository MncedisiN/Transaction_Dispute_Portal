namespace DisputePortal.Api.Models.Responses;

public class ContactChallengeDto
{
    public Guid Id { get; set; }
    public string MaskedDestination { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }

    public ContactChallengeDto()
    {
    }

    public ContactChallengeDto(Guid Id, string MaskedDestination, DateTimeOffset ExpiresAt)
    {
        this.Id = Id;
        this.MaskedDestination = MaskedDestination;
        this.ExpiresAt = ExpiresAt;
    }
}
