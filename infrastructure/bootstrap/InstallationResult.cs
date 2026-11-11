namespace DisputePortal.Setup;

public class InstallationResult
{
    public List<InitialAccess> InitialAccess { get; set; } = [];
    public string Fingerprint { get; set; } = "";
    public int ServiceCount { get; set; }
}

public class InitialAccess
{
    public string Email { get; set; } = "";
    public string[] Roles { get; set; } = [];
    public string Password { get; set; } = "";
}

public class InitialAdministrator
{
    public string Email { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string[] Roles { get; set; } = [];
    public string PasswordHash { get; set; } = "";
}
