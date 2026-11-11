using DisputePortal.Platform.Contracts;
using DisputePortal.Platform.Infrastructure;
using DisputePortal.Api.Interfaces.Finance;
using DisputePortal.Api.Interfaces.Security;
using DisputePortal.Platform.Infrastructure.Interfaces;

namespace DisputePortal.Api.Platform;

public static class PlatformExtensions
{
    public static void AddFinancialPlatform(this WebApplicationBuilder builder)
    {
        builder.Configuration.AddJsonFile("Configuration/assessment-data.json", false, false);
        builder.Configuration.AddIntegrationDefaults();
        if (!builder.Configuration.GetValue<bool>("Platform:Enabled"))
        { 
            builder.Services.AddServiceSecurity(new ServiceOptions { Name = "identity" }, ServiceSecurity.InternalServiceScheme);
            return;
        }

        var path = builder.Configuration["Platform:SettingsPath"] ?? throw new InvalidOperationException("Platform:SettingsPath must identify the configured identity service settings.");
        builder.Configuration.AddJsonFile(Path.GetFullPath(path), false, true).AddEnvironmentVariables();
        var options = builder.Configuration.GetSection("Platform").Get<ServiceOptions>() ?? throw new InvalidOperationException("Identity service settings are required.");
        options.ValidateServiceConfiguration();
        if (options.Name != "identity")
        {
            throw new InvalidOperationException("Service settings belong to a different boundary.");
        }

        options.DatabaseProvider = DisputePortal.Api.Repository.Data.DatabaseDialect.ParseProvider(options.DatabaseProvider).ToString();
          builder.Configuration["Database:Provider"] = options.DatabaseProvider;
        builder.Configuration["ConnectionStrings:" + options.DatabaseProvider] = options.ConnectionString;
        if (!builder.Environment.IsDevelopment())
        {
            TransportSecurity.ValidateServices(options);
        }

        if (builder.Configuration.GetValue<bool>("Platform:CanonicalDisputes"))
        {
            builder.Services.AddScoped<IDisputeFinancialCommands, DisputeFinancialCommandService>();
        }

        builder.Services.AddScoped<ProfileDocumentService>();
        builder.Services.AddSingleton(options);
        builder.Services.AddServiceSecurity(options, ServiceSecurity.InternalServiceScheme);
        builder.Services.AddSingleton<ServiceDatabase>();
        var assessment = builder.Configuration.GetRequiredSection("AssessmentData")
            .Get<DisputePortal.Api.Models.Configuration.AssessmentDataOptions>()!;
        if (AssessmentDataAccess.IsEnabled(builder.Configuration, builder.Environment))
        {
            assessment.Validate();
        }

        builder.Services.AddSingleton(assessment);
        builder.Services.AddHostedService<DisputePortal.Api.Service.BackgroundJobs.AssessmentAccountWorker>();
        builder.Services.AddSingleton<EventBus>();
        builder.Services.AddSingleton<IDomainEventConsumer, IdentityProjectionConsumer>();
        builder.Services.AddHostedService<EventWorker>();
        builder.Services.AddServiceHttpClient(builder.Configuration);
        builder.Services.AddScoped<IRegistrationProfileValidator, RegistrationProfileValidator>();
        builder.Services.AddScoped<IRegistrationSessionIssuer, RegistrationSessionIssuer>();
    }
}
