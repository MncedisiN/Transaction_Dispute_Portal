using DisputePortal.Api.Configuration;
using Microsoft.AspNetCore.SignalR;
using DisputePortal.Api.Interfaces.Repositories;
using DisputePortal.Api.Interfaces.Security;

namespace DisputePortal.Api.Realtime;

public class RealtimeSessionWorker(
    RealtimeConnections connections,
    IServiceScopeFactory scopes,
    ILogger<RealtimeSessionWorker> logger,
    ApiRuntimeOptions runtime,
    IHubContext<PortalRealtimeHub>? hub = null) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(runtime.RealtimeSessionCheckSeconds));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                var snapshot = connections.Snapshot();
                if (snapshot.Count == 0)
                {
                    continue;
                }

                try
                {
                    using var scope = scopes.CreateScope();
                    var authentication = scope.ServiceProvider.GetRequiredService<IAuthenticationRepository>();
                    var sessions = scope.ServiceProvider.GetRequiredService<IRealtimeSessionReader>();
                    await ValidateConnections(authentication, sessions, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception error)
                { 
                    foreach (var connection in snapshot)
                    {
                        connection.Abort();
                        connections.RemoveRealtimeConnection(connection.ConnectionId);
                    }

                    logger.LogWarning("Live sessions closed because validation is unavailable ({FailureType})", error.GetType().Name);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    public async Task ValidateConnections(IAuthenticationRepository authentication, IRealtimeSessionReader sessions, CancellationToken ct)
    {
        foreach (var group in connections.Snapshot().GroupBy(connection => connection.UserId))
        {
            var current = await authentication.GetCurrentUser(group.Key, ct);
            var signature = current is null ? null : RealtimeConnections.PermissionSignature(current.Permissions);
            foreach (var connection in group)
            {
                var sessionValid = !connection.SessionId.HasValue
                    || await sessions.IsActive(connection.UserId, connection.SessionId.Value, DateTimeOffset.UtcNow, ct);
                if (!sessionValid || signature != connection.Permissions)
                {
                    if (hub is not null)
                    {
                        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        timeout.CancelAfter(TimeSpan.FromSeconds(runtime.RealtimeSessionEndTimeoutSeconds));
                        try
                        {
                            await hub.Clients.Client(connection.ConnectionId).SendAsync(
                                "SessionEnded",
                                new { reason = "session_ended" },
                                timeout.Token);
                        }
                        catch (Exception) when (!ct.IsCancellationRequested)
                        {
                        }
                    }

                    connection.Abort();
                    connections.RemoveRealtimeConnection(connection.ConnectionId);
                }
            }
        }
    }
}
