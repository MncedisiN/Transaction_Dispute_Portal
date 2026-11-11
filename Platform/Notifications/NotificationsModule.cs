using DisputePortal.Platform.Infrastructure;
using DisputePortal.Platform.Infrastructure.Interfaces;
using DisputePortal.Platform.Notifications.Interfaces;

namespace DisputePortal.Platform.Notifications;

public class NotificationsModule(INotificationStore store) : IServiceModule
{
    public string Schema => NotificationStore.Schema;

    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<INotificationStore, NotificationStore>();
        services.AddSingleton<IDomainEventConsumer, NotificationConsumer>();
        services.AddSingleton<IPersistentJobRunner, InAppDelivery>();
    }

    public void MapServiceEndpoints(WebApplication app)
    {
        app.MapGet(
            "/notifications",
            async (HttpContext ctx, CancellationToken ct) =>
        {
            ctx.User.RequireCaller("identity");
            return await store.GetCustomerNotifications(ctx.User.UserId(), ct);
        }).RequireAuthorization();
        app.MapPost(
            "/notifications/{id:guid}/read",
            async (Guid id, HttpContext ctx, CancellationToken ct) =>
        {
            ctx.User.RequireCaller("identity");
            await store.MarkNotificationRead(id, ctx.User.UserId(), ct);
            return Results.NoContent();
        }).RequireAuthorization();
    }
}
