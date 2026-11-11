using Dapper;
using DisputePortal.Api.Models.Domain;
using DisputePortal.Api.Interfaces.Data;
using DisputePortal.Api.Interfaces.Security;
using DisputePortal.Platform.Infrastructure.Sql;

namespace DisputePortal.Api.Repository;

public class RealtimeSessionReader(IDatabaseConnectionFactory connections, SessionPolicy policy, DisputePortal.Api.Configuration.ApiRuntimeOptions runtime) : IRealtimeSessionReader
{
    public async Task<bool> IsActive(Guid user, Guid session, DateTimeOffset now, CancellationToken ct)
    {
        await using var connection = await connections.OpenConnection(ct);
        // Security heartbeats must never count as user activity or extend idle time.
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/RealtimeSessionReader/CountActiveUserSessionsForUserAndSession"),
            new
            {
                User = user,
                Session = session,
                Now = now,
                IdleCutoff = now.AddMinutes(-policy.IdleMinutes),
                AbsoluteCutoff = now.AddMinutes(-policy.AbsoluteMinutes)
            },
            commandTimeout: runtime.RealtimeDatabaseTimeoutSeconds,
            cancellationToken: ct)) == 1;
    }
}
