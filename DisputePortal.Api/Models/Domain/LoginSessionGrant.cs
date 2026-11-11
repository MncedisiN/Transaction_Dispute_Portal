namespace DisputePortal.Api.Models.Domain;

public class LoginSessionGrant
{
    public Guid UserId { get; set; }
    public Guid SessionId { get; set; }
    public bool OtpVerified { get; set; }
    public string? DeviceCookie { get; set; }
    public DateTimeOffset? DeviceExpiresAt { get; set; }
    public bool RememberedDeviceUsed { get; set; }

    public LoginSessionGrant()
    {
    }

    public LoginSessionGrant(
        Guid UserId,
        Guid SessionId,
        bool OtpVerified,
        string? DeviceCookie = null,
        DateTimeOffset? DeviceExpiresAt = null,
        bool RememberedDeviceUsed = false)
    {
        this.UserId = UserId;
        this.SessionId = SessionId;
        this.OtpVerified = OtpVerified;
        this.DeviceCookie = DeviceCookie;
        this.DeviceExpiresAt = DeviceExpiresAt;
        this.RememberedDeviceUsed = RememberedDeviceUsed;
    }
}
