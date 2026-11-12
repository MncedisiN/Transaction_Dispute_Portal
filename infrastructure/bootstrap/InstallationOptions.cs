namespace DisputePortal.Setup;

public class InstallationOptions
{
    public string StateDirectory { get; set; } = "";
    public string SettingsDirectory { get; set; } = "";
    public string StaffFile { get; set; } = Path.Combine(AppContext.BaseDirectory, "staff-users.json");
    public string PublicOrigin { get; set; } = "http://localhost:8080";
    public string LocalAddress { get; set; } = "";
    public int HttpsPort { get; set; } = 8443;
    public int CertificatePort { get; set; } = 8081;
    public string PushSubject { get; set; } = "";
    public string PrivateSettingsFile { get; set; } = "";
    public string ApiDatabaseProvider { get; set; } = "SqlServer";
    public bool PopulateAssessmentAccounts { get; set; } = true;
    public bool RequireContactVerification { get; set; }
    public bool ExternalMessagesEnabled { get; set; }
}
