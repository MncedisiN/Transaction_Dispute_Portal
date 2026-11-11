using Dapper;
using DisputePortal.Api.Models.Configuration;
using DisputePortal.Api.Service.Banking;
using DisputePortal.Platform.Contracts;
using DisputePortal.Platform.Infrastructure;
using DisputePortal.Platform.Infrastructure.Interfaces;
using DisputePortal.Platform.Infrastructure.Sql;

namespace DisputePortal.Api.Service.BackgroundJobs;

public class AssessmentAccountWorker(
    ServiceDatabase database,
    IServiceClient client,
    AssessmentDataOptions options,
    IConfiguration configuration,
    IHostEnvironment environment,
    ILogger<AssessmentAccountWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!AssessmentDataAccess.IsEnabled(configuration, environment))
        {
            return;
        }

        long after = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var connection = await database.OpenServiceDatabaseConnection(stoppingToken);
                var provider = database.Dialect is SqlServerDialect ? "SqlServer" : "PostgreSql";
                var rows = (await connection.QueryAsync<EmptyAccount>(new CommandDefinition(
                    SqlScripts.ReadSqlScript(provider + "/Queries/Api/AssessmentAccountWorker/GetEmptyCustomerAccounts"),
                    new { After = after, options.BatchSize }, cancellationToken: stoppingToken))).ToArray();
                foreach (var row in rows)
                {
                    after = row.AccountKey;
                    try
                    {
                        await PrepareCustomerAccounts(row.UserId, stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception error)
                    {
                        logger.LogWarning("Assessment account preparation deferred ({FailureType})", error.GetType().Name);
                    }
                }

                if (rows.Length < options.BatchSize)
                {
                    after = 0;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception error)
            {
                logger.LogWarning("Assessment account scan deferred ({FailureType})", error.GetType().Name);
            }

            await Task.Delay(TimeSpan.FromSeconds(options.PollSeconds), stoppingToken);
        }
    }

    public async Task PrepareCustomerAccounts(Guid user, CancellationToken ct)
    {
        if (!AssessmentDataAccess.IsEnabled(configuration, environment))
        {
            return;
        }

        var actor = AssessmentAccountHistory.Customer(user);
        var accounts = await client.SendServiceRequest<AccountRecord[]>(
            "accounts", HttpMethod.Get, $"/internal/owners/{user}/accounts", ct: ct);
        var settings = await client.SendServiceRequest<PlatformSettings>("configuration", HttpMethod.Get, "/settings", ct: ct);
        var country = CountryCurrencyPolicy.Current(settings);
        foreach (var template in options.Accounts)
        {
            if (!(settings.AccountTypes ?? []).Contains(template.Type))
            {
                throw new InvalidOperationException("Assessment account types must be enabled by the saved country policy.");
            }

            var account = accounts.SingleOrDefault(item => item.Type == template.Type
                && item.CountryCode == country.Code && item.Currency == country.DefaultCurrency && item.Status != "Closed");
            if (account is null)
            {
                await client.SendServiceRequest<AccountRecord>("accounts", HttpMethod.Post, "/accounts",
                    new OpenAccountRequest(AssessmentAccountHistory.StableId(user, "open-" + template.Type), template.Type, "", ""),
                    actor, ct);
            }
        }

        foreach (var account in accounts.Where(item => item.Status == "Active"
            && item.CountryCode == country.Code && item.Currency == country.DefaultCurrency
            && item.LedgerBalance == 0 && item.ReservedBalance == 0))
        {
            var template = options.Accounts.SingleOrDefault(item => item.Type == account.Type);
            if (template is null)
            {
                continue;
            }

            await using var connection = await database.OpenServiceDatabaseConnection(ct);
            await using var transaction = await connection.BeginTransactionAsync(ct);
            await RelationalDialect.LockOperations(connection, transaction, database.Dialect.OperationsLockSql, ct);
            var eventId = AssessmentAccountHistory.StableId(account.Id, "request");
            var existing = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COUNT(*) FROM service_outbox WHERE event_id = @Id",
                new { Id = eventId }, transaction, cancellationToken: ct));
            var populated = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                """
                SELECT COUNT(*)
                FROM "transactions"."transactions" AS t
                INNER JOIN "accounts"."accounts" AS a ON a."id" = t."accountId"
                WHERE a."publicId" = @Id
                """, new { Id = account.Id }, transaction, cancellationToken: ct));
            var projected = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                """
                SELECT COUNT(*)
                FROM "accounts"."accounts"
                WHERE "publicId" = @Id
                """, new { Id = account.Id }, transaction, cancellationToken: ct));
            if (existing == 0 && populated == 0 && projected == 1)
            {
                var value = DomainEvent.CreateDomainEvent("identity", EventTypes.AssessmentAccountRequested,
                    AssessmentAccountHistory.Create(account, template, options.HistoryDays, DateTimeOffset.UtcNow), account.Id);
                value.EventId = eventId;
                await ServiceDatabase.SaveDomainEventToOutbox(connection, transaction, value, ct);
            }

            await transaction.CommitAsync(ct);
        }
    }

    private class EmptyAccount
    {
        public long AccountKey { get; set; }
        public Guid UserId { get; set; }
    }
}
