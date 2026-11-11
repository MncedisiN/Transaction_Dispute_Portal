namespace DisputePortal.Api.Extensions;

public static class PortalConfigurationExtensions
{
    public static IConfigurationBuilder AddPortalDevelopmentSettings(
        this IConfigurationBuilder configuration,
        IHostEnvironment environment,
        string[] args)
    {
        if (!environment.IsDevelopment())
        {
            return configuration;
        }
         
        return configuration.AddJsonFile("appsettings.Development.local.json", optional: true, reloadOnChange: false).AddEnvironmentVariables().AddCommandLine(args);
    }
}
