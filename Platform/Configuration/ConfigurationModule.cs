using DisputePortal.Platform.Contracts;
using DisputePortal.Platform.Infrastructure;
using System.Security.Claims;
using DisputePortal.Platform.Configuration.Interfaces;
using DisputePortal.Platform.Infrastructure.Interfaces;

namespace DisputePortal.Platform.Configuration;

public class ConfigurationModule(ISettingsStore settings, CountryOperationStore operations) : IServiceModule
{
    public string Schema => SettingsStore.Schema + ConfigurationBootstrapSchema.PostgreSql;

    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<SettingsStore>();
        services.AddSingleton<ISettingsStore>(provider => provider.GetRequiredService<SettingsStore>());
        services.AddSingleton<IServiceInitializer>(provider => provider.GetRequiredService<SettingsStore>());
        services.AddSingleton<CountryOperationStore>();
        services.AddSingleton<SettingsVersionStore>();
        services.AddSingleton<IDomainEventConsumer, CountryOperationConsumer>();
    }

    public void MapServiceEndpoints(WebApplication app)
    {
        app.MapGet(
            "/settings",
            async (HttpContext context, CancellationToken ct) =>
        {
            context.User.RequireCaller("identity", "accounts", "ledger", "transactions", "payments", "notifications", "scheduling", "debt");
            return await settings.GetPlatformSettings(ct);
        }).RequireAuthorization();
        app.MapPost(
            "/settings",
            async (SettingsUpdate request, HttpContext context, CancellationToken ct) =>
        {
            context.User.RequireCaller("identity");
            context.User.RequireAdmin();
            return await settings.UpdatePlatformSettings(request, context.User.UserId(), ct);
        }).RequireAuthorization();
        app.MapGet(
            "/settings/history",
            async (int? page, int? pageSize, string? search, HttpContext context, SettingsVersionStore versions, CancellationToken ct) =>
        {
            context.User.RequireCaller("identity");
            context.User.RequireAdmin();
            return await versions.History(page ?? 1, pageSize ?? 20, search, ct);
        }).RequireAuthorization();
        app.MapPost(
            "/settings/preview",
            async (SettingsUpdate request, HttpContext context, SettingsStore store, CancellationToken ct) =>
        {
            context.User.RequireCaller("identity");
            context.User.RequireAdmin();
            return await store.Preview(request, context.User.UserId(), ct);
        }).RequireAuthorization();
        app.MapPost(
            "/settings/versions/{id:guid}/removal-preview",
            async (Guid id, SettingsRemovalRequest request, HttpContext context, SettingsVersionStore versions, CancellationToken ct) =>
        {
            context.User.RequireCaller("identity");
            context.User.RequireAdmin();
            return await versions.SoftDeleteSettingsVersion(context.User.UserId(), id, request, true, ct);
        }).RequireAuthorization();
        app.MapPost(
            "/settings/versions/{id:guid}/remove",
            async (Guid id, SettingsRemovalRequest request, HttpContext context, SettingsVersionStore versions, CancellationToken ct) =>
        {
            context.User.RequireCaller("identity");
            context.User.RequireAdmin();
            return await versions.SoftDeleteSettingsVersion(context.User.UserId(), id, request, false, ct);
        }).RequireAuthorization();
        app.MapPost(
            "/country-operations",
            async (CountryOperationRequest request, HttpContext context, CancellationToken ct) =>
        {
            context.User.RequireCaller("identity", "accounts", "payments", "transactions", "scheduling", "debt", "ledger");
            return await operations.Begin(request, context.User.FindFirstValue("caller")!, ct);
        }).RequireAuthorization();
        app.MapPost(
            "/country-operations/{sourceKind}/{requestId:guid}/settle",
            async (string sourceKind, Guid requestId, Guid? customerId, HttpContext context, CancellationToken ct) =>
        {
            context.User.RequireCaller("identity", "accounts", "payments", "transactions", "scheduling", "debt", "ledger");
            await operations.Settle(sourceKind, requestId, context.User.FindFirstValue("caller")!, ct, customerId);
            return Results.NoContent();
        }).RequireAuthorization();
        app.MapGet(
            "/country-operations/operations/{operationId:guid}",
            async (Guid operationId, HttpContext context, CancellationToken ct) =>
        {
            context.User.RequireCaller("identity", "accounts", "payments", "transactions", "scheduling", "debt", "ledger");
            return await operations.GetCountryOperation(operationId, ct);
        }).RequireAuthorization();
    }
}
