using DisputePortal.Platform.Contracts;
using DisputePortal.Platform.Infrastructure;
using DisputePortal.Platform.Accounts.Interfaces;
using DisputePortal.Platform.Infrastructure.Interfaces;
using DisputePortal.Platform.Infrastructure.Models;

namespace DisputePortal.Platform.Accounts;

public class AccountsModule(IAccountQueries queries, IAccountCommands commands, IServiceClient client) : IServiceModule
{
    public string Schema => AccountStore.Schema;

    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<AccountImportStore>();
        services.AddSingleton<AccountStore>();
        services.AddSingleton<IAccountQueries>(sp => sp.GetRequiredService<AccountStore>());
        services.AddSingleton<IAccountCommands>(sp => sp.GetRequiredService<AccountStore>());
        services.AddSingleton<IDomainEventConsumer, AccountConsumer>();
        services.AddSingleton<IPersistentJobRunner, AccountCountryPolicyJob>();
    }

    public void MapServiceEndpoints(WebApplication app)
    {
        app.MapGet(
            "/operations/country-eligibility",
            async (HttpContext context, AccountStore store, CancellationToken ct) =>
        {
            context.User.RequireCaller("configuration");
            return await store.CountryEligibility(ct);
        }).RequireAuthorization();
        app.MapGet(
            "/internal/owners/{id:guid}/accounts",
            async (Guid id, HttpContext ctx, CancellationToken ct) =>
        {
            ctx.User.RequireCaller("identity");
            return await queries.GetCustomerAccounts(id, ct);
        }).RequireAuthorization();
        if (app.Configuration.GetValue<bool>("Platform:AllowBookMigration"))
        {
            app.MapPost(
                "/migration/book",
                async (AccountHistorySnapshot book, HttpContext ctx, AccountImportStore imports, CancellationToken ct) =>
            {
                ctx.User.RequireCaller("identity");
                ctx.User.RequireAdmin();
                if (ctx.User.UserId() != book.ActorId)
                {
                    throw new PlatformException(403, "actor_mismatch", "The migration operator does not match.");
                }

                return await imports.Import(book, ct);
            }).RequireAuthorization();
        }

        app.MapGet(
            "/internal/accounts/{id:guid}",
            async (Guid id, HttpContext ctx, CancellationToken ct) =>
        {
            ctx.User.RequireCaller("identity");
            return await queries.GetAccountById(id, ct);
        }).RequireAuthorization();
        app.MapGet(
            "/accounts",
            async (HttpContext ctx, CancellationToken ct) =>
        {
            ctx.User.RequireCaller("identity");
            return await queries.GetCustomerAccounts(ctx.User.UserId(), ct);
        }).RequireAuthorization();
        app.MapGet(
            "/accounts/{id:guid}",
            async (Guid id, HttpContext ctx, CancellationToken ct) =>
        {
            ctx.User.RequireCaller("identity", "transactions", "ledger", "payments", "scheduling", "debt");
            var a = await queries.GetAccountById(id, ct);
            if (ctx.User.FindFirst("caller")?.Value == "identity" && a.UserId != ctx.User.UserId())
            {
                throw new PlatformException(404, "account_missing", "Account not found.");
            }

            return a;
        }).RequireAuthorization();
        app.MapGet(
            "/accounts/resolve/{number}",
            async (string number, HttpContext ctx, CancellationToken ct) =>
        {
            ctx.User.RequireCaller("identity");
            _ = ctx.User.UserId();
            return await queries.FindAccountByNumber(number, ct);
        }).RequireAuthorization();
        app.MapPost(
            "/accounts",
            async (OpenAccountRequest request, HttpContext ctx, CancellationToken ct) =>
        {
            ctx.User.RequireCaller("identity");
            return await commands.OpenCustomerAccount(ctx.User.UserId(), request, ct);
        }).RequireAuthorization();
        app.MapGet(
            "/accounts/{id:guid}/statement",
            async (Guid id, HttpContext ctx, CancellationToken ct) =>
        {
            ctx.User.RequireCaller("identity");
            if ((await queries.GetAccountById(id, ct)).UserId != ctx.User.UserId())
            {
                throw new PlatformException(404, "account_missing", "Account not found.");
            }

            return await client.SendServiceRequest<System.Text.Json.JsonElement>(
                    "ledger",
                    HttpMethod.Get,
                    $"/accounts/{id}/entries",
                    actor: ctx.User,
                    ct: ct);
        }).RequireAuthorization();
    }
}
