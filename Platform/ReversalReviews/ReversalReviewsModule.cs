using DisputePortal.Platform.Contracts;
using DisputePortal.Platform.Infrastructure;
using DisputePortal.Platform.ReversalReviews.Interfaces;
using DisputePortal.Platform.Infrastructure.Interfaces;
using DisputePortal.Platform.Infrastructure.Sql;

namespace DisputePortal.Platform.ReversalReviews;

public class ReversalReviewsModule(IReversalReviewStore store) : IServiceModule
{
    public string Schema => ReversalReviewStore.Schema;

    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IReversalReviewStore, ReversalReviewStore>();
        services.AddSingleton<IDomainEventConsumer, ReversalRequestConsumer>();
        services.AddSingleton<IPersistentJobRunner, ScheduledReversalReviewWorker>();
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
                        SqlScripts.ReadSqlScript("PostgreSql/Queries/ReversalReviews/ReversalReviewsModule/CheckReversalReviewsExist"),
                        cancellationToken: ct));
        }).RequireAuthorization();
        app.MapGet(
            "/reviews",
            async (HttpContext ctx, CancellationToken ct) =>
        {
            ctx.User.RequireCaller("identity");
            ctx.User.RequireAdmin();
            return await store.GetReversalReviews(ct);
        }).RequireAuthorization();
    }
}
