using DisputePortal.Setup;

try
{
    var result = AssessmentInstaller.Initialize(new InstallationOptions
    {
        StateDirectory = "/state",
        SettingsDirectory = "/settings",
        PublicOrigin = Environment.GetEnvironmentVariable("PORTAL_PUBLIC_URL") ?? "http://localhost:8080",
        LocalAddress = Environment.GetEnvironmentVariable("PORTAL_LAN_IP") ?? "",
        HttpsPort = int.Parse(Environment.GetEnvironmentVariable("PORTAL_HTTPS_PORT") ?? "8443", System.Globalization.CultureInfo.InvariantCulture),
        PushSubject = Environment.GetEnvironmentVariable("PUSH_SUBJECT") ?? "",
        PrivateSettingsFile = Environment.GetEnvironmentVariable("PORTAL_PRIVATE_SETTINGS") ?? "",
        ApiDatabaseProvider = Environment.GetEnvironmentVariable("API_DATABASE_PROVIDER") ?? "SqlServer",
        PopulateAssessmentAccounts = !bool.TryParse(Environment.GetEnvironmentVariable("POPULATE_ASSESSMENT_ACCOUNTS"), out var populate) || populate,
        RequireContactVerification = bool.TryParse(Environment.GetEnvironmentVariable("REQUIRE_CONTACT_VERIFICATION"), out var verification) && verification,
        ExternalMessagesEnabled = bool.TryParse(Environment.GetEnvironmentVariable("EXTERNAL_MESSAGES_ENABLED"), out var messaging) && messaging
    });

    foreach (var user in result.InitialAccess)
    {
        Console.WriteLine($"Initial assessment login: {user.Email} | {string.Join(", ", user.Roles)} | {user.Password}");
    }

    Console.WriteLine($"Installation identity ready for {result.ServiceCount} services ({result.Fingerprint[..12]}). Existing keys and passwords retained.");
    return 0;
}
catch (Exception)
{
    Console.Error.WriteLine("Installation setup failed. Preserve existing volumes and check installation settings; credentials were not replaced.");
    return 1;
}
