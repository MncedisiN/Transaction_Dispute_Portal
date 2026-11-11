namespace DisputePortal.Api.Models.Domain;

public class LoginFlowInfo
{
    public DateTimeOffset ResendAvailableAt { get; set; }
    public DateTimeOffset FlowExpiresAt { get; set; }
    public bool CanRememberDevice { get; set; }
    public int RememberedDeviceDays { get; set; }

    public LoginFlowInfo()
    {
    }

    public LoginFlowInfo(DateTimeOffset ResendAvailableAt, DateTimeOffset FlowExpiresAt, bool CanRememberDevice, int RememberedDeviceDays)
    {
        this.ResendAvailableAt = ResendAvailableAt;
        this.FlowExpiresAt = FlowExpiresAt;
        this.CanRememberDevice = CanRememberDevice;
        this.RememberedDeviceDays = RememberedDeviceDays;
    }
}
