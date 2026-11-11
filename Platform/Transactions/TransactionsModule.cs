using DisputePortal.Platform.Infrastructure;
using DisputePortal.Platform.Contracts;
using DisputePortal.Platform.Infrastructure.Interfaces;
using DisputePortal.Platform.Infrastructure.Models;
using DisputePortal.Platform.Transactions.Interfaces;
using DisputePortal.Platform.Infrastructure.Sql;

namespace DisputePortal.Platform.Transactions;

public class TransactionsModule(ITransactionStore store, IServiceClient client) : IServiceModule
{
    public string Schema => TransactionStore.Schema + HistoryStore.Schema + ReceivedActivityStore.Schema;

    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<ReceivedActivityStore>();
        services.AddSingleton<IDomainEventConsumer>(sp => sp.GetRequiredService<ReceivedActivityStore>());
        services.AddSingleton<IHistoryStore, HistoryStore>();
        services.AddSingleton<ITransactionStore, TransactionStore>();
        services.AddSingleton<IDomainEventConsumer, PaymentIntentConsumer>();
        services.AddSingleton<IDomainEventConsumer, CaseFinancialConsumer>();
        services.AddSingleton<IPersistentJobRunner, TransactionProcessor>();
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
                        SqlScripts.ReadSqlScript("PostgreSql/Queries/Transactions/TransactionsModule/CheckTransactionsExist"),
                        cancellationToken: ct));
        }).RequireAuthorization();
        app.MapGet(
            "/internal/transactions/{id:guid}",
            async (Guid id, HttpContext ctx, CancellationToken ct) =>
        {
            ctx.User.RequireCaller("identity");
            return await store.GetTransactionById(id, null, ct);
        }).RequireAuthorization();

        if (AssessmentDataAccess.IsEnabled(app.Configuration, app.Environment))
        {
            app.MapPost(
                "/internal/assessment/accounts",
                async (AccountHistorySnapshot history, HttpContext context, IHistoryStore store, CancellationToken ct) =>
            {
                AssessmentDataAccess.AuthorizeImport(context.User, history);
                var account = await client.SendServiceRequest<AccountRecord>(
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
                async (AccountHistorySnapshot book, HttpContext ctx, IHistoryStore history, CancellationToken ct) =>
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

                return await history.Import(book, ct);
            }).RequireAuthorization();
        }

        app.MapPost(
            "/reversals",
            async (ReversalPreparation r, HttpContext ctx, CancellationToken ct) =>
        {
            ctx.User.RequireCaller("debt");
            var original = await store.GetTransactionById(r.OriginalTransactionId, null, ct);
            if (original.Status != "Completed"
                    || !(r.Kind == "Refund"
                    && original.Direction == "Debit"
                    && original.Kind is not ("Refund" or "Recall")
                    || r.Kind == "Recall"
                    && original.Kind == "Refund"))
            {
                throw new PlatformException(409, "reversal_ineligible", "The original transaction is ineligible.");
            }

            var account = await client.SendServiceRequest<AccountRecord>(
                    "accounts",
                    HttpMethod.Get,
                    $"/accounts/{original.AccountId}",
                    ct: ct);
            CountryOperation? operation = null;
            try
            {
                operation = await client.SendServiceRequest<CountryOperation>(
                        "configuration",
                        HttpMethod.Get,
                        $"/country-operations/operations/{r.RequestId}",
                        ct: ct);
            }
            catch (PlatformException error) when (error.Code == "operation_required")
            {
                operation = await client.SendServiceRequest<CountryOperation>(
                        "configuration",
                        HttpMethod.Post,
                        "/country-operations",
                        new CountryOperationRequest
                        {
                            RequestId = r.RequestId,
                            OperationId = r.RequestId,
                            SourceKind = "Reversal",
                            CountryCode = account.CountryCode,
                            Currency = original.Currency,
                            OriginalTransactionId = r.OriginalTransactionId
                        },
                        ct: ct);
            }

            if (operation.SourceKind != "Reversal"
                    || operation.RequestId != r.RequestId
                    || operation.OriginalTransactionId != r.OriginalTransactionId
                    || operation.Currency != original.Currency
                    || operation.CountryCode != account.CountryCode)
            {
                throw new PlatformException(409, "request_conflict", "The reversal does not match its captured country policy.");
            }

            // Replay may read a previously settled transaction; a settled claim can never create a new one.
            if (operation.SettledAt is not null)
            {
                return await store.GetTransactionById(operation.OperationId, null, ct);
            }

            return await store.Reverse(r, original, ct);
        }).RequireAuthorization();
        app.MapGet(
            "/transactions",
            async (HttpContext ctx, CancellationToken ct) =>
        {
            ctx.User.RequireCaller("identity");
            return await store.GetCustomerTransactions(ctx.User.UserId(), ct);
        }).RequireAuthorization();
        app.MapGet(
            "/transactions/{id:guid}",
            async (Guid id, HttpContext ctx, CancellationToken ct) =>
        {
            ctx.User.RequireCaller("identity", "debt", "scheduling");
            return await store.GetTransactionById(id, ctx.User.FindFirst("caller")?.Value == "identity" ? ctx.User.UserId() : null, ct);
        }).RequireAuthorization();
    }
}
