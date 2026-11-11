namespace DisputePortal.Api.Configuration;

public static class AssessmentSecurity
{
    public static bool AllowsUnverifiedContacts(IConfiguration? configuration, IHostEnvironment? environment) =>
        environment?.IsDevelopment() == true
        && configuration?.GetValue<bool>("Assessment:SkipContactVerification") == true;
}
