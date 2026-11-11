using DisputePortal.Platform.Contracts;
using DisputePortal.Platform.Infrastructure;
using DisputePortal.Platform.Ledger.Domain;
using DisputePortal.Platform.Infrastructure.Interfaces;
using DisputePortal.Platform.Infrastructure.Models;
using DisputePortal.Platform.Ledger.Interfaces;
using DisputePortal.Platform.Infrastructure.Sql;

namespace DisputePortal.Platform.Ledger;

public class LedgerModule(ILedgerCommands commands, ILedgerQueryStore queries) : IServiceModule
{
    public string Schema => LedgerSchema.Sql + AccountHistoryImportStore.Schema;

    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IAccountHistoryImportStore, AccountHistoryImportStore>();
        services.AddSingleton<PostgresLedgerRepository>();
        services.AddSingleton<ILedgerCommandStore>(sp => sp.GetRequiredService<PostgresLedgerRepository>());
        services.AddSingleton<ILedgerQueryStore>(sp => sp.GetRequiredService<PostgresLedgerRepository>());
        services.AddSingleton<ILedgerAccountWriter>(sp => sp.GetRequiredService<PostgresLedgerRepository>());
        services.AddSingleton<ILedgerExpiryStore>(sp => sp.GetRequiredService<PostgresLedgerRepository>());
        services.AddSingleton<IFinancialConfiguration, RemoteFinancialConfiguration>();
        services.AddSingleton<ILedgerCommands, LedgerCommandService>();
        services.AddSingleton<IPostingStrategy, TransferPostingStrategy>();
        services.AddSingleton<IPostingStrategy, DepositPostingStrategy>();
        services.AddSingleton<IPostingStrategy, WithdrawalPostingStrategy>();
        services.AddSingleton<IPostingStrategy, RefundPostingStrategy>();
        services.AddSingleton<IPostingStrategy, RecallPostingStrategy>();
        services.AddSingleton<IPostingPlanFactory, PostingPlanFactory>();
        services.AddSingleton<IDomainEventConsumer, LedgerAccountConsumer>();
        services.AddSingleton<IPersistentJobRunner, ReservationExpiryJob>();
        services.AddSingleton<IPersistentJobRunner, LedgerCountryPolicyJob>();
    }

    public void MapServiceEndpoints(WebApplication app)
    {
        app.MapPost(
            "/internal/book-imports/imported-accounts",
            async (Guid[] accountIds, HttpContext context, ServiceDatabase db, CancellationToken ct) =>
        {
            context.User.RequireCaller("identity");
            if (accountIds.Length > 500 || accountIds.Any(id => id == Guid.Empty))
            {
                throw new PlatformException(400, "account_batch_invalid", "Use at most 500 valid account identifiers.");
            }

            await using var connection = await db.OpenServiceDatabaseConnection(ct);
            return (await Dapper.SqlMapper.QueryAsync<Guid>(
                    connection,
                    new Dapper.CommandDefinition(
                        SqlScripts.ReadSqlScript("PostgreSql/Queries/Ledger/LedgerModule/GetImportedAccountIds"),
                        new
                        {
                            AccountIds = accountIds
                        },
                        cancellationToken: ct))).ToArray();
        }).RequireAuthorization();
        app.MapGet(
            "/operations/country-eligibility",
            async (HttpContext context, ServiceDatabase db, CancellationToken ct) =>
        {
            context.User.RequireCaller("configuration");
            await using var connection = await db.OpenServiceDatabaseConnection(ct);
            return await Dapper.SqlMapper.QuerySingleAsync<CountryChangeEligibility>(
                    connection,
                    new Dapper.CommandDefinition(
                        """
                        SELECT
                            COALESCE(bool_or(balance <> 0), FALSE) AS HasNonzeroBalances,
                            COALESCE(bool_or(reserved <> 0), FALSE) AS HasReserves
                        FROM ledger."accounts"
                        WHERE kind = 'Customer'
                        """,
                        cancellationToken: ct));
        }).RequireAuthorization();
        app.MapGet(
            "/owners/{id:guid}/balances",
            async (Guid id, HttpContext ctx, ServiceDatabase db, CancellationToken ct) =>
        {
            ctx.User.RequireCaller("identity", "payments");
            await using var c = await db.OpenServiceDatabaseConnection(ct);
            return await Dapper.SqlMapper.QueryAsync<BalanceSnapshot>(
                    c,
                    new Dapper.CommandDefinition(
                        """
                        SELECT
                            "publicId" AS AccountId,
                            "customerPublicId" AS UserId,
                            currency,
                            balance AS LedgerBalance,
                            reserved AS ReservedBalance,
                            version
                        FROM ledger."accounts"
                        WHERE "customerPublicId" = @Id
                        AND kind = 'Customer'
                        """,
                        new
                        {
                            Id = id
                        },
                        cancellationToken: ct));
        }).RequireAuthorization();

        if (AssessmentDataAccess.IsEnabled(app.Configuration, app.Environment))
        {
            app.MapPost(
                "/internal/assessment/accounts",
                async (AccountHistorySnapshot history, HttpContext context, IAccountHistoryImportStore store, IServiceClient serviceClient, CancellationToken ct) =>
            {
                AssessmentDataAccess.AuthorizeImport(context.User, history);
                var account = await serviceClient.SendServiceRequest<AccountRecord>(
                    "accounts", HttpMethod.Get, "/accounts/" + history.AccountId, ct: ct);
                if (account.UserId != history.UserId
                    || account.Number != history.Number
                    || account.Type != history.AccountType
                    || account.CountryCode != history.CountryCode
                    || account.Currency != history.Currency
                    || account.Status != "Active")
                {
                    throw new PlatformException(409, "assessment_account_mismatch", "The assessment account is not eligible.");
                }

                return await store.Import(history, ct);
            }).RequireAuthorization();
        }

        if (app.Configuration.GetValue<bool>("Platform:AllowBookMigration"))
        {
            app.MapPost(
                "/migration/book",
                async (AccountHistorySnapshot book, HttpContext ctx, IAccountHistoryImportStore imports, CancellationToken ct) =>
            {
                ctx.User.RequireCaller("identity");
                ctx.User.RequireAdmin();
                if (book.SourceSystem != "dispute-portal-sql")
                {
                    throw new PlatformException(400, "import_source_invalid", "Use an approved account history source.");
                }
                if (ctx.User.UserId() != book.ActorId)
                {
                    throw new PlatformException(403, "actor_mismatch", "The migration operator does not match.");
                }

                return await imports.Import(book, ct);
            }).RequireAuthorization();
        }

        app.MapPost(
            "/reservations",
            async (ReserveRequest request, HttpContext ctx, CancellationToken ct) =>
        {
            ctx.User.RequireCaller("transactions", "payments");
            return await commands.ReserveAccountFunds(request, ct);
        }).RequireAuthorization();
        app.MapPost(
            "/journals",
            async (PostJournalRequest request, HttpContext ctx, CancellationToken ct) =>
        {
            ctx.User.RequireCaller("transactions");
            return await commands.Post(request, ct);
        }).RequireAuthorization();
        app.MapGet(
            "/journals/{id:guid}",
            async (Guid id, HttpContext ctx, CancellationToken ct) =>
        {
            ctx.User.RequireCaller("transactions", "payments", "identity");
            return await queries.GetJournal(id, ct);
        }).RequireAuthorization();
        app.MapPost(
            "/reservations/{id:guid}/release",
            async (Guid id, HttpContext ctx, CancellationToken ct) =>
        {
            ctx.User.RequireCaller("transactions", "payments");
            await commands.ReleaseReservedAccountFunds(id, ct);
            return Results.NoContent();
        }).RequireAuthorization();
        app.MapGet(
            "/accounts/{id:guid}/entries",
            async (Guid id, HttpContext ctx, CancellationToken ct) =>
        {
            ctx.User.RequireCaller("accounts");
            return await queries.Entries(id, ctx.User.UserId(), ct);
        }).RequireAuthorization();
        app.MapGet(
            "/reconciliation",
            async (HttpContext ctx, CancellationToken ct) =>
        {
            ctx.User.RequireCaller("identity");
            ctx.User.RequireAdmin();
            return await queries.Reconcile(ct);
        }).RequireAuthorization();
    }
}
