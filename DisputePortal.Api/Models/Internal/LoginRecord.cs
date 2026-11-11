namespace DisputePortal.Api.Models.Internal;

public class LoginRecord
{
    public Guid UserId { get; set; }
    public string LoginIdentifier { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string AccountStatus { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string[] Roles { get; set; } = [];
    public string[] Permissions { get; set; } = [];

    public LoginRecord()
    {
    }

    public LoginRecord(
        Guid UserId,
        string LoginIdentifier,
        string PasswordHash,
        string AccountStatus,
        string DisplayName,
        string[] Roles,
        string[] Permissions)
    {
        this.UserId = UserId;
        this.LoginIdentifier = LoginIdentifier;
        this.PasswordHash = PasswordHash;
        this.AccountStatus = AccountStatus;
        this.DisplayName = DisplayName;
        this.Roles = Roles;
        this.Permissions = Permissions;
    }

    public LoginRecord(LoginRecord other)
    {
        UserId = other.UserId;
        LoginIdentifier = other.LoginIdentifier;
        PasswordHash = other.PasswordHash;
        AccountStatus = other.AccountStatus;
        DisplayName = other.DisplayName;
        Roles = other.Roles;
        Permissions = other.Permissions;
    }
}
