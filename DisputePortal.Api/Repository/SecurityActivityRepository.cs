using Dapper;
using DisputePortal.Api.Models.Exceptions;
using DisputePortal.Api.Models.Responses;
using DisputePortal.Api.Models.Domain;
using System.Net;
using DisputePortal.Api.Interfaces.Data;
using DisputePortal.Api.Interfaces.Repositories;
using DisputePortal.Platform.Infrastructure.Sql;

namespace DisputePortal.Api.Repository;

public class SecurityActivityRepository(
    IDatabaseConnectionFactory factory,
    IDatabaseDialect databaseDialect,
    SessionPolicy? sessionPolicy = null) : RepositoryBase(factory, databaseDialect), ISecurityActivityRepository
{
    private SessionPolicy SessionSettings => sessionPolicy ?? new SessionPolicy();

    public async Task<SignInDetailDto?> GetSignInDetail(Guid user, Guid eventId, CancellationToken ct)
    {
        await using var c = await connections.OpenConnection(ct);
        // Audit observations retain public UUIDs after a session has disappeared;
        // resolve that observed identifier when displaying its current activity.
        var sql = SqlScripts.RenderSqlScript(
            "Shared/Queries/Api/SecurityActivityRepository/GetOwnedSuccessfulSignInForSessionReview",
            ("ExistsAsBoolean", dialect.ExistsAsBoolean(SqlScripts.ReadSqlScript("Shared/Queries/Api/SecurityActivityRepository/CheckUserSessionsForUser"))));
        return await c.QuerySingleOrDefaultAsync<SignInDetailDto>(new CommandDefinition(
            sql,
            new
            {
                User = user,
                Id = eventId,
                Now = DateTimeOffset.UtcNow,
                IdleCutoff = DateTimeOffset.UtcNow.AddMinutes(-SessionSettings.IdleMinutes),
                AbsoluteCutoff = DateTimeOffset.UtcNow.AddMinutes(-SessionSettings.AbsoluteMinutes)
            },
            cancellationToken: ct));
    }

    public async Task<IReadOnlyList<SecurityActivityDto>> GetRecentSignIns(Guid user, CancellationToken ct)
    {
        await using var c = await connections.OpenConnection(ct);
        return (await c.QueryAsync<SecurityActivityDto>(new CommandDefinition(
            SqlScripts.RenderSqlScript(
            "Shared/Queries/Api/SecurityActivityRepository/GetRecentSuccessfulSignIns",
            ("PaginationClause", dialect.PaginationClause)),
            new
            {
                User = user,
                Offset = 0,
                PageSize = 5
            },
            cancellationToken: ct))).AsList();
    }

    public async Task RecordSecurityEvent(
        Guid? user,
        string eventType,
        string outcome,
        string ip,
        string userAgent,
        Guid? session,
        CancellationToken ct)
    {
        var agent = userAgent[..Math.Min(userAgent.Length, 512)];
        var device = agent.Contains("Android", StringComparison.OrdinalIgnoreCase) ? "Android" : agent.Contains(
            "iPhone",
            StringComparison.OrdinalIgnoreCase) ? "iPhone" : agent.Contains("iPad", StringComparison.OrdinalIgnoreCase) ? "iPad" : agent.Contains(
            "Windows",
            StringComparison.OrdinalIgnoreCase) ? "Windows" : agent.Contains("Macintosh", StringComparison.OrdinalIgnoreCase) ? "Mac" : agent.Contains(
            "Linux",
            StringComparison.OrdinalIgnoreCase) ? "Linux" : "Unknown";
        var browser = agent.Contains("Edg/") ? "Edge" : agent.Contains("Firefox/") ? "Firefox" : agent.Contains("Chrome/") ? "Chrome" : agent.Contains("Safari/") ? "Safari" : "Unknown";
        var location = IPAddress.TryParse(ip, out var address) && IPAddress.IsLoopback(address) ? "Local development" : "Unavailable";
        await using var c = await connections.OpenConnection(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await c.ExecuteAsync(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/SecurityActivityRepository/InsertSecurityEvent"),
            new
            {
                Id = Guid.NewGuid(),
                Correlation = System.Diagnostics.Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString(),
                Session = session,
                User = user,
                Event = eventType,
                Outcome = outcome,
                Ip = ip,
                Agent = agent,
                Device = device,
                Browser = browser,
                Location = location,
                Now = DateTimeOffset.UtcNow
            },
            tx,
            cancellationToken: ct));
        if (session is { } sid)
        {
            await c.ExecuteAsync(new CommandDefinition(
                SqlScripts.ReadSqlScript("Shared/Queries/Api/SecurityActivityRepository/UpdateUserSessionsForUser"),
                new
                {
                    Id = sid,
                    User = user,
                    Ip = ip,
                    Device = device,
                    Browser = browser
                },
                tx,
                cancellationToken: ct));
        }

        await tx.CommitAsync(ct);
    }

    public async Task<IReadOnlyList<SecurityActivityDto>> GetSecurityActivity(Guid? user, int page, int pageSize, CancellationToken ct)
    {
        await using var c = await connections.OpenConnection(ct);
        return (await c.QueryAsync<SecurityActivityDto>(new CommandDefinition(
            SqlScripts.RenderSqlScript(
            "Shared/Queries/Api/SecurityActivityRepository/GetCustomerSecurityActivity",
            ("PaginationClause", dialect.PaginationClause)),
            new
            {
                User = user,
                Offset = PageOffset(page, pageSize),
                PageSize = pageSize
            },
            cancellationToken: ct))).AsList();
    }

    public async Task<IReadOnlyList<ActiveSessionDto>> GetActiveSessions(Guid user, CancellationToken ct)
    {
        await using var c = await connections.OpenConnection(ct);
        return (await c.QueryAsync<ActiveSessionDto>(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/SecurityActivityRepository/GetUserSessionsForUser"),
            new
            {
                User = user,
                Now = DateTimeOffset.UtcNow,
                IdleCutoff = DateTimeOffset.UtcNow.AddMinutes(-SessionSettings.IdleMinutes),
                AbsoluteCutoff = DateTimeOffset.UtcNow.AddMinutes(-SessionSettings.AbsoluteMinutes)
            },
            cancellationToken: ct))).AsList();
    }

    public async Task RevokeOwnedSession(Guid user, Guid session, CancellationToken ct)
    {
        await using var c = await connections.OpenConnection(ct);
        if (await c.ExecuteAsync(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/SecurityActivityRepository/RevokeUserSessionsForUser"),
            new
            {
                Id = session,
                User = user,
                Now = DateTimeOffset.UtcNow
            },
            cancellationToken: ct)) != 1)
        {
            throw new PortalNotFoundException();
        }
    }
}
