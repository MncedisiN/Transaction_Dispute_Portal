namespace DisputePortal.Api.Models.Domain;

public class LoginVerificationPolicy
{
    public bool LoginOtpEnabled { get; set; } = true;
    public bool RequireStaffMfa { get; set; } = true;
    public bool RememberedDevicesEnabled { get; set; } = true;
    public int RememberedDeviceDays { get; set; } = 30;
    public int MaximumDevices { get; set; } = 5;
    public bool AllowStaffPreference { get; set; } = false;

    public bool CustomerOnly(IEnumerable<string> roles)
    {
        var values = roles.ToArray();
        return values.Length > 0 && values.All(role => role == "customer");
    }

    public bool RequiresCode(IEnumerable<string> roles) => LoginOtpEnabled || (RequireStaffMfa && !CustomerOnly(roles));

    public bool RequiresCode(IEnumerable<string> roles, bool? preference)
    {
        var values = roles.ToArray();
        return LoginOtpEnabled && preference.HasValue && (CustomerOnly(values) || AllowStaffPreference) ? preference.Value : RequiresCode(values);
    }

    public bool CanChangePreference(IEnumerable<string> roles) => LoginOtpEnabled && (CustomerOnly(roles) || AllowStaffPreference);

    public bool CanRemember(IEnumerable<string> roles) => RememberedDevicesEnabled && LoginOtpEnabled && CustomerOnly(roles);

    public bool CanRemember(IEnumerable<string> roles, bool? preference) => CanRemember(roles) && RequiresCode(roles, preference);

    public string? PreferencePolicyMessage(IEnumerable<string> roles)
    {
        var values = roles.ToArray();
        if (!LoginOtpEnabled)
        {
            return RequiresCode(values) ? "Two-step verification is required for your staff account." : "Login verification is disabled for this portal.";
        }

        return CanChangePreference(values) ? null : "Two-step verification is required for your staff account.";
    }

    public void ValidateLoginVerificationPolicy()
    {
        if (RememberedDeviceDays is < 1 or > 30 || MaximumDevices is < 1 or > 10)
        {
            throw new ArgumentException("Remembered-device limits must be 1–30 days and 1–10 devices.");
        }
    }
}
