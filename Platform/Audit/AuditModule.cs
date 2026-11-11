using DisputePortal.Platform.Infrastructure;
using DisputePortal.Platform.Audit.Interfaces;
using DisputePortal.Platform.Infrastructure.Interfaces;

namespace DisputePortal.Platform.Audit;

public class AuditModule(IAuditStore store) : IServiceModule
{
    public string Schema => AuditStore.Schema;

    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IAuditStore, AuditStore>();
        services.AddSingleton<IDomainEventConsumer, AuditConsumer>();
    }

    public void MapServiceEndpoints(WebApplication app)
    {
        app.MapGet(
            "/events",
            async (Guid? correlation, HttpContext ctx, CancellationToken ct) =>
        {
            ctx.User.RequireCaller("identity");
            ctx.User.RequireAdmin();
            return await store.GetAuditEntries(correlation, ct);
        }).RequireAuthorization();
    }
}
