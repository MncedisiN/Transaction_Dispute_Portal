using DisputePortal.Platform.Contracts;
using DisputePortal.Platform.Infrastructure;
using DisputePortal.Platform.Infrastructure.Interfaces;
using DisputePortal.Platform.Scheduling.Interfaces;
using DisputePortal.Platform.Infrastructure.Sql;

namespace DisputePortal.Platform.Scheduling;

public class SchedulingModule(IScheduleStore store) : IServiceModule
{
    public string Schema => ScheduleStore.Schema;

    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IScheduleStore, ScheduleStore>();
        services.AddSingleton<IPersistentJobRunner, ScheduleRunner>();
        services.AddSingleton<IDomainEventConsumer, ScheduledReversalOutcomeConsumer>();
    }

    public void MapServiceEndpoints(WebApplication app)
    {
        app.MapGet(
            "/operations/country-eligibility",
            async (HttpContext context, ServiceDatabase db, CancellationToken ct) =>
        {
            context.User.RequireCaller("configuration");
            await using var connection = await db.OpenServiceDatabaseConnection(ct);
            return await Dapper.SqlMapper.QuerySingleAsync<CountryChangeEligibility>(
                    connection,
                    new Dapper.CommandDefinition(
                        SqlScripts.ReadSqlScript("PostgreSql/Queries/Scheduling/SchedulingModule/CheckReversalsExist"),
                        cancellationToken: ct));
        }).RequireAuthorization();
        app.MapGet(
            "/reversals",
            async (HttpContext ctx, CancellationToken ct) =>
        {
            ctx.User.RequireCaller("identity");
            ctx.User.RequireAdmin();
            return await store.GetScheduledReversalReviews(ct);
        }).RequireAuthorization();
        app.MapPost(
            "/reversals",
            async (ScheduleReversal r, HttpContext ctx, CancellationToken ct) =>
        {
            ctx.User.RequireCaller("identity");
            ctx.User.RequireAdmin();
            return await store.ScheduleReversalReview(r, ctx.User.UserId(), ct);
        }).RequireAuthorization();
    }
}
