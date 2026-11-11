namespace DisputePortal.Api.Models.Responses;

public class IdentityChallengeDto
{
    public Guid ChallengeId { get; set; }
    public string Channel { get; set; } = string.Empty;
    public string MaskedDestination { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public bool RequiresVerification { get; set; } = true;
    public DateTimeOffset? ResendAvailableAt { get; set; }
    public DateTimeOffset? FlowExpiresAt { get; set; }
    public bool CanRememberDevice { get; set; }
    public int RememberedDeviceDays { get; set; } = 30;

    public IdentityChallengeDto()
    {
    }

    public IdentityChallengeDto(
        Guid ChallengeId,
        string Channel,
        string MaskedDestination,
        DateTimeOffset ExpiresAt,
        bool RequiresVerification = true,
        DateTimeOffset? ResendAvailableAt = null,
        DateTimeOffset? FlowExpiresAt = null,
        bool CanRememberDevice = false,
        int RememberedDeviceDays = 30)
    {
        this.ChallengeId = ChallengeId;
        this.Channel = Channel;
        this.MaskedDestination = MaskedDestination;
        this.ExpiresAt = ExpiresAt;
        this.RequiresVerification = RequiresVerification;
        this.ResendAvailableAt = ResendAvailableAt;
        this.FlowExpiresAt = FlowExpiresAt;
        this.CanRememberDevice = CanRememberDevice;
        this.RememberedDeviceDays = RememberedDeviceDays;
    }

    public IdentityChallengeDto(IdentityChallengeDto other)
    {
        ChallengeId = other.ChallengeId;
        Channel = other.Channel;
        MaskedDestination = other.MaskedDestination;
        ExpiresAt = other.ExpiresAt;
        RequiresVerification = other.RequiresVerification;
        ResendAvailableAt = other.ResendAvailableAt;
        FlowExpiresAt = other.FlowExpiresAt;
        CanRememberDevice = other.CanRememberDevice;
        RememberedDeviceDays = other.RememberedDeviceDays;
    }
}
