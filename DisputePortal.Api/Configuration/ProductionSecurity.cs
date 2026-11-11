using DisputePortal.Api.Repository.Data;

namespace DisputePortal.Api.Configuration;

public static class ProductionSecurity
{
    public static void ValidateProductionSecurity(
        IConfiguration configuration,
        IHostEnvironment environment,
        DatabaseConfiguration? database = null)
    {
        if (environment.IsDevelopment())
        {
            return;
        }

        if (configuration.GetValue<bool>("Assessment:PopulateAccounts"))
        {
            throw new InvalidOperationException("Assessment account data is available only in Development.");
        }

        if (configuration.GetValue<bool>("Assessment:SkipContactVerification"))
        {
            throw new InvalidOperationException("Assessment registration without contact verification is available only in Development.");
        }

        DisputePortal.Platform.Infrastructure.TransportSecurity.ValidateServerConfiguration(configuration);
        if (configuration.GetValue("ForwardedHeaders_Enabled", false)
            || string.Equals(
            Environment.GetEnvironmentVariable("ASPNETCORE_FORWARDEDHEADERS_ENABLED"),
            "true",
            StringComparison.OrdinalIgnoreCase)
            || string.Equals(
            Environment.GetEnvironmentVariable("DOTNET_FORWARDEDHEADERS_ENABLED"),
            "true",
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Automatic unrestricted forwarded headers are not permitted. Configure explicit trusted proxies or networks.");
        }

        if (configuration.GetValue("DevelopmentSeed:Enabled", false))
        {
            throw new InvalidOperationException("Development fixtures must be disabled outside Development.");
        }

        if (!configuration.GetValue("Authentication:RequireStaffMfa", true))
        {
            throw new InvalidOperationException("Staff MFA cannot be disabled outside Development.");
        }

        if (configuration.GetValue("Authentication:AllowStaffLoginPreference", false))
        {
            throw new InvalidOperationException("Staff login verification preferences are available only in Development.");
        }

        foreach (var origin in configuration.GetSection("AllowedOrigins").Get<string[]>() ?? [])
            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)
            || uri.Scheme != "https"
            || uri.AbsolutePath != "/"
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Fragment)
            || origin.Contains('*'))
            {
                throw new InvalidOperationException("Production CORS origins must be explicit HTTPS origins.");
            }

        if (string.IsNullOrWhiteSpace(configuration["DataProtection:KeysDirectory"])
            || string.IsNullOrWhiteSpace(configuration["DataProtection:CertificatePath"]))
        {
            throw new InvalidOperationException("Configure persistent Data Protection keys and a key-encryption certificate for production.");
        }

        (database ?? DatabaseConfiguration.ResolveDatabaseConfiguration(configuration)).ValidateProduction();
    }
}
