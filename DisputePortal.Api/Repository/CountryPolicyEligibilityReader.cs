using System.Data;
using Dapper;
using DisputePortal.Api.Repository.Data;
using DisputePortal.Platform.Contracts;
using DisputePortal.Api.Interfaces.Data;
using DisputePortal.Platform.Infrastructure.Interfaces;
using DisputePortal.Platform.Infrastructure.Models;
using DisputePortal.Platform.Infrastructure.Sql;

namespace DisputePortal.Api.Repository;

public class CountryPolicyEligibilityReader(
    IDatabaseConnectionFactory connections,
    DatabaseConfiguration database,
    IServiceClient? platform = null)
{
    public async Task<CountryChangeEligibility> GetCountryChangeBlockers(CancellationToken ct)
    {
        await using var connection = await connections.OpenConnection(ct);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct); 
        var tryFence = database.Provider == DatabaseProvider.SqlServer ? SqlScripts.ReadSqlScript("SqlServer/Queries/Api/CountryPolicyEligibilityReader/AcquireOperationLock") : """
        SELECT
            pg_try_advisory_xact_lock(hashtextextended('DisputePortalOperations', 0))
        """;
        var acquired = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            tryFence,
            transaction: transaction,
            cancellationToken: ct));
        if (!acquired)
        {
            await transaction.RollbackAsync(ct);
            return new CountryChangeEligibility
            {
                HasUnfinishedOperations = true,
                Blocker = "Account or financial updates are in progress. Try changing country again shortly."
            };
        }

        var sql = SqlScripts.ReadSqlScript("Shared/Queries/Api/CountryPolicyEligibilityReader/GetPendingCountryChangeOperations");
        var row = await connection.QuerySingleAsync<PendingCountryWork>(new CommandDefinition(
            sql,
            transaction: transaction,
            cancellationToken: ct));
        // Canonical Ledger owns current balances. Only the prior local books
        // need this additional check; imported snapshots must not be counted twice.
        var sourceBalances = (await connection.QueryAsync<UnimportedBalance>(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/CountryPolicyEligibilityReader/GetAccountsWithBalancesReservesOrExternalLinks"),
            transaction: transaction,
            cancellationToken: ct))).ToArray();
        var imported = new HashSet<Guid>();
        if (sourceBalances.Length > 0)
        {
            if (platform is null)
            {
                throw new PlatformException(
                    503,
                    "country_balances_unavailable",
                    "Account balances could not be verified. Try again shortly.");
            }

            foreach (var batch in sourceBalances.Select(balance => balance.AccountId).Chunk(500))
            {
                var accounts = await platform.SendServiceRequest<Guid[]>(
                    "ledger",
                    HttpMethod.Post,
                    "/internal/book-imports/imported-accounts",
                    batch,
                    ct: ct);
                imported.UnionWith(accounts);
            }
        }

        var unimported = sourceBalances.Where(balance => !imported.Contains(balance.AccountId)).ToArray();
        var nonzero = unimported.Any(balance => balance.CurrentAmount != 0);
        var reserves = unimported.Any(balance => balance.ReservedAmount != 0);
        var unknown = unimported.Any(balance => balance.Unknown != 0);
        await transaction.CommitAsync(ct);
        string? blocker = null;
        if (row.PendingRefunds > 0)
        {
            blocker = "A refund or recall is still processing. Wait for its confirmed outcome before changing country.";
        }
        else if (row.UnfinishedOperations > 0 || row.PendingDelivery > 0)
        {
            blocker = "A financial operation is still processing. Wait for its confirmed outcome before changing country.";
        }

        if (nonzero || reserves || unknown)
        {
            blocker = "Existing account balances must be reconciled and settled before changing country.";
        }

        return new CountryChangeEligibility
        {
            HasNonzeroBalances = nonzero,
            HasReserves = reserves,
            HasPendingRefunds = row.PendingRefunds > 0,
            HasUnfinishedOperations = row.UnfinishedOperations > 0 || row.PendingDelivery > 0 || unknown,
            Blocker = blocker
        };
    }

    private class UnimportedBalance
    {
        public Guid AccountId { get; set; }
        public decimal CurrentAmount { get; set; }
        public decimal ReservedAmount { get; set; }
        public int Unknown { get; set; }
    }

    private class PendingCountryWork
    {
        public int PendingRefunds { get; set; }
        public int UnfinishedOperations { get; set; }
        public int PendingDelivery { get; set; }
    }
}
