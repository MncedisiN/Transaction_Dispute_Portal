using Microsoft.Extensions.DependencyInjection.Extensions;
using DisputePortal.Api.Interfaces.Observability;

namespace DisputePortal.Api.Observability;

public static class TelemetryRegistration
{
    public static IServiceCollection AddPortalMonitoring(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetRequiredSection("Monitoring").Get<TelemetryOptions>() ?? throw new InvalidOperationException("Monitoring configuration is required.");
        options.ValidateTelemetryConfiguration();
        services.AddSingleton(options);
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<PortalTelemetry>();
        services.AddSingleton<EndpointMetricCatalog>();
        services.AddSingleton<MetricAggregator>();
        services.AddSingleton<IApiMetricStore, ApiMetricStore>();
        services.AddHostedService<HttpMetricListener>();
        services.AddHostedService<MetricPersistenceWorker>();
        return services;
    }
}
