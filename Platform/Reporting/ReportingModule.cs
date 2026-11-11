using DisputePortal.Platform.Infrastructure;
using DisputePortal.Platform.Infrastructure.Interfaces;
using DisputePortal.Platform.Reporting.Interfaces;

namespace DisputePortal.Platform.Reporting;

public class ReportingModule(IReportingStore store) : IServiceModule
{
    public string Schema => ReportingStore.Schema;

    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IReportingStore, ReportingStore>();
        services.AddSingleton<IDomainEventConsumer, ReportingConsumer>();
    }

    public void MapServiceEndpoints(WebApplication app)
    {
        app.MapGet(
            "/summary",
            async (HttpContext ctx, CancellationToken ct) =>
        {
            ctx.User.RequireCaller("identity");
            ctx.User.RequireAdmin();
            return await store.Summary(ct);
        }).RequireAuthorization();
    }
}
