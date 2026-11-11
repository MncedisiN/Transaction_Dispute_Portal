using DisputePortal.Platform.Contracts;
using DisputePortal.Platform.Infrastructure;
using DisputePortal.Platform.Infrastructure.Interfaces;
using DisputePortal.Platform.Payments.Interfaces;

namespace DisputePortal.Platform.Payments;

public class PaymentsModule(IPaymentService service, IPaymentStore store) : IServiceModule
{
    public string Schema => PaymentStore.Schema;

    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IPaymentStore, PaymentStore>();
        services.AddSingleton<PaymentService>();
        services.AddSingleton<IPaymentService>(sp => sp.GetRequiredService<PaymentService>());
        services.AddSingleton<IPersistentJobRunner>(sp => sp.GetRequiredService<PaymentService>());
        services.AddSingleton<IDomainEventConsumer, PaymentOutcomeConsumer>();
    }

    public void MapServiceEndpoints(WebApplication app)
    {
        app.MapGet(
            "/operations/country-eligibility",
            async (HttpContext context, CancellationToken ct) =>
        {
            context.User.RequireCaller("configuration");
            return await store.CountryEligibility(ct);
        }).RequireAuthorization();
        app.MapGet(
            "/payments/requests/{id:guid}",
            async (Guid id, HttpContext ctx, CancellationToken ct) =>
        {
            ctx.User.RequireCaller("identity");
            return await store.Lookup(ctx.User.UserId(), id, ct);
        }).RequireAuthorization();
        app.MapPost(
            "/payments/requests/{id:guid}/cancel",
            async (Guid id, HttpContext ctx, CancellationToken ct) =>
        {
            ctx.User.RequireCaller("identity");
            return await service.CancelUnknown(ctx.User.UserId(), id, ct);
        }).RequireAuthorization();
        app.MapPost(
            "/quotes",
            async (PaymentQuoteRequest r, HttpContext ctx, CancellationToken ct) =>
        {
            ctx.User.RequireCaller("identity");
            return await service.Quote(r, ctx.User.UserId(), ct);
        }).RequireAuthorization();
        app.MapPost(
            "/payments",
            async (PaymentRequest r, HttpContext ctx, CancellationToken ct) =>
        {
            ctx.User.RequireCaller("identity");
            return await service.CreateInternalTransfer(r, ctx.User.UserId(), ct);
        }).RequireAuthorization();
        app.MapGet(
            "/payments",
            async (HttpContext ctx, CancellationToken ct) =>
        {
            ctx.User.RequireCaller("identity");
            return await store.GetCustomerPayments(ctx.User.UserId(), ct);
        }).RequireAuthorization();
    }
}
