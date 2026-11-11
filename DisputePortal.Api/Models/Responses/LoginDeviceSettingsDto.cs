namespace DisputePortal.Api.Models.Responses;

public class LoginDeviceSettingsDto
{
    public bool CanRememberDevice { get; set; }
    public bool RequiresTwoStep { get; set; }
    public int RememberedDeviceDays { get; set; }
    public IReadOnlyList<LoginDeviceDto> Devices { get; set; } = [];
    public bool CanChangeVerification { get; set; }
    public bool? LoginOtpEnabled { get; set; }
    public string? VerificationPolicyMessage { get; set; }

    public LoginDeviceSettingsDto()
    {
    }

    public LoginDeviceSettingsDto(
        bool CanRememberDevice,
        bool RequiresTwoStep,
        int RememberedDeviceDays,
        IReadOnlyList<LoginDeviceDto> Devices,
        bool CanChangeVerification = false,
        bool? LoginOtpEnabled = null,
        string? VerificationPolicyMessage = null)
    {
        this.CanRememberDevice = CanRememberDevice;
        this.RequiresTwoStep = RequiresTwoStep;
        this.RememberedDeviceDays = RememberedDeviceDays;
        this.Devices = Devices;
        this.CanChangeVerification = CanChangeVerification;
        this.LoginOtpEnabled = LoginOtpEnabled;
        this.VerificationPolicyMessage = VerificationPolicyMessage;
    }
}
