using Dapper;
using DisputePortal.Api.Interfaces.Data;
using DisputePortal.Api.Interfaces.Repositories;
using DisputePortal.Platform.Infrastructure.Sql;

namespace DisputePortal.Api.Repository;

public class RealtimeRecipients(IDatabaseConnectionFactory connections, DisputePortal.Api.Configuration.ApiRuntimeOptions runtime) : IRealtimeRecipients
{
    public async Task<Guid?> Owner(string entity, Guid id, CancellationToken ct)
    {
        var sql = entity switch
        {
            "dispute" => SqlScripts.ReadSqlScript("Shared/Queries/Api/RealtimeRecipients/GetCaseCustomerForRealtimeDelivery"),
            "support" => SqlScripts.ReadSqlScript("Shared/Queries/Api/RealtimeRecipients/GetConversations"),
            "transaction" => SqlScripts.ReadSqlScript("Shared/Queries/Api/RealtimeRecipients/GetTransactions"),
            "deletion" => SqlScripts.ReadSqlScript("Shared/Queries/Api/RealtimeRecipients/GetAccountDeletionRequests"),
            _ => null
        };
        if (sql is null)
        {
            return null;
        }

        await using var connection = await connections.OpenConnection(ct);
        return await connection.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition(
            sql,
            new { Id = id },
            commandTimeout: runtime.RealtimeDatabaseTimeoutSeconds,
            cancellationToken: ct));
    }
}
