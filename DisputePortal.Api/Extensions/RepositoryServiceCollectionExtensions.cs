using DisputePortal.Api.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using DisputePortal.Api.Repository;
using DisputePortal.Api.Repository.Data;
using DisputePortal.Api.Interfaces.Data;
using DisputePortal.Api.Interfaces.Repositories;
using DisputePortal.Api.Interfaces.Security;

namespace DisputePortal.Api.Extensions;

public static class RepositoryServiceCollectionExtensions
{
    public static IServiceCollection AddPortalInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        DatabaseConfiguration? database = null)
    {
        var runtime = ApiRuntimeOptions.LoadRuntimeOptions(configuration);
        services.TryAddSingleton(runtime);
        services.TryAddSingleton(TimeZoneInfo.FindSystemTimeZoneById(runtime.BusinessTimeZone));
        database ??= DatabaseConfiguration.ResolveDatabaseConfiguration(configuration);
        services.AddSingleton(database);
        services.AddSingleton<IDatabaseDialect>(DatabaseDialect.GetDatabaseDialect(database.Provider));
        services.AddSingleton<IDatabaseConnectionFactory>(new DatabaseConnectionFactory(database.Provider, database.ConnectionString));
        services.AddSingleton<DatabaseInitializer>();
        services.AddSingleton<IEvidencePolicyRepository, EvidencePolicyRepository>();
        services.AddScoped<DisputePortal.Api.Middleware.EvidenceUploadLimitsFilter>();
        services.AddSingleton<DisputePortal.Api.Interfaces.Security.IEvidenceScanner, DisputePortal.Api.Service.Security.ApiEvidenceScanner>();
        bool Flag(string key, bool fallback) => configuration[key] is { } value ? bool.Parse(value) : fallback;
        int Limit(string key, int fallback) => configuration[key] is { } value ? int.Parse(
            value,
            System.Globalization.CultureInfo.InvariantCulture) : fallback;
        var loginPolicy = new DisputePortal.Api.Models.Domain.LoginVerificationPolicy
        {
            LoginOtpEnabled = Flag("Authentication:LoginOtpEnabled", true),
            RequireStaffMfa = Flag("Authentication:RequireStaffMfa", true),
            RememberedDevicesEnabled = Flag("Authentication:RememberedDevices:Enabled", true),
            RememberedDeviceDays = Limit("Authentication:RememberedDevices:Days", 30),
            MaximumDevices = Limit("Authentication:RememberedDevices:MaximumDevices", 5),
            AllowStaffPreference = Flag("Authentication:AllowStaffLoginPreference", false)
        };
        loginPolicy.ValidateLoginVerificationPolicy();
        services.AddSingleton(loginPolicy);
        services.AddScoped<ILoginSecurityRepository, LoginSecurityRepository>();
        services.AddScoped<IStaffTransactionRepository, StaffTransactionRepository>();
        services.AddScoped<IFinanceRepository, FinanceRepository>();
        services.AddScoped<PolicyVersionRepository>();
        services.AddScoped<CaseStatusRepository>();
        services.AddScoped<IPortalSettingsRepository, PortalSettingsRepository>();
        services.AddScoped<AuthenticationRepository>();
        services.AddScoped<IRealtimeRecipients, RealtimeRecipients>();
        services.AddScoped<IRealtimeSessionReader, RealtimeSessionReader>();
        services.AddScoped<IPasswordChangeRepository>(provider => provider.GetRequiredService<AuthenticationRepository>());
        services.AddScoped<IAuthenticationRepository>(provider => provider.GetRequiredService<AuthenticationRepository>());
        services.AddScoped<CountryPolicyEligibilityReader>();
        services.AddScoped<BankingRepository>();
        services.AddScoped<ITransactionRepository>(provider => provider.GetRequiredService<BankingRepository>());
        services.AddScoped<IBankFeedStore>(provider => provider.GetRequiredService<BankingRepository>());
        services.AddScoped<DisputeOperationsRepository>();
        services.AddScoped<IOperationsRepository>(provider => provider.GetRequiredService<DisputeOperationsRepository>());
        services.AddScoped<DisputeRiskRepository>();
        services.AddScoped<IDisputeRiskRepository>(provider => provider.GetRequiredService<DisputeRiskRepository>());
        services.AddScoped<DisputeRepository>();
        services.AddScoped<IDisputeRepository>(provider => provider.GetRequiredService<DisputeRepository>());
        services.AddScoped<NotificationRepository>();
        services.AddScoped<INotificationRepository>(provider => provider.GetRequiredService<NotificationRepository>());
        services.AddScoped<INotificationDeliveryRepository>(provider => provider.GetRequiredService<NotificationRepository>());
        services.AddScoped<AuditRepository>();
        services.AddScoped<IAuditRepository>(provider => provider.GetRequiredService<AuditRepository>());
        services.AddScoped<UserAdministrationRepository>();
        services.AddScoped<IUserAdministrationRepository>(provider => provider.GetRequiredService<UserAdministrationRepository>());
        services.AddScoped<EvidenceRepository>();
        services.AddScoped<IEvidenceRepository>(provider => provider.GetRequiredService<EvidenceRepository>());
        services.AddScoped<SupportRepository>();
        services.AddScoped<ISupportRepository>(provider => provider.GetRequiredService<SupportRepository>());
        services.AddScoped<ISupportReadRepository>(provider => provider.GetRequiredService<SupportRepository>());
        services.AddScoped<ProfileRepository>();
        services.AddScoped<IProfileRepository>(provider => provider.GetRequiredService<ProfileRepository>());
        services.AddScoped<ContactVerificationRepository>();
        services.AddScoped<IContactVerificationRepository>(provider => provider.GetRequiredService<ContactVerificationRepository>());
        services.AddScoped<IdentityVerificationRepository>();
        services.AddScoped<IIdentityVerificationRepository>(provider => provider.GetRequiredService<IdentityVerificationRepository>());
        services.AddScoped<SecurityActivityRepository>();
        services.AddScoped<ISecurityActivityRepository>(provider => provider.GetRequiredService<SecurityActivityRepository>());
        services.AddScoped<AccountManagementRepository>();
        services.AddScoped<IAccountManagementRepository>(provider => provider.GetRequiredService<AccountManagementRepository>());
        return services;
    }
}
