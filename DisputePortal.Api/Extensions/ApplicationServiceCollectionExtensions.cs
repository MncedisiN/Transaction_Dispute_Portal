using DisputePortal.Api.Models.Validators;
using DisputePortal.Api.Service.Disputes;
using FluentValidation;
using DisputePortal.Api.Interfaces.Disputes;

namespace DisputePortal.Api.Extensions;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddPortalApplication(this IServiceCollection services)
    {
        services.AddScoped<IDisputeService, DisputeService>();
        services.AddScoped<Service.Security.SignInService>();
        services.AddScoped<Interfaces.Repository.IDisputeInputPolicyStore, Repository.DisputeInputPolicyStore>();
        services.AddValidatorsFromAssemblyContaining<SignInValidator>();
        services.AddScoped<Service.Banking.BankFeedService>();
        services.AddScoped<Interfaces.Accounts.IAccountClosureService, Service.Accounts.AccountClosureService>();
        return services;
    }
}
