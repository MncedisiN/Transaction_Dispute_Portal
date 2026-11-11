using System.Security.Claims;
using DisputePortal.Api.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using DisputePortal.Api.Interfaces.Repositories;
using DisputePortal.Api.Interfaces.Security;

namespace DisputePortal.Api.Realtime;

[Authorize]
public class PortalRealtimeHub(
    IAuthenticationRepository authentication,
    IRealtimeSessionReader sessions,
    RealtimeConnections connections,
    IConfiguration configuration) : Hub
{
    public const string EndpointPath = "/api/v1/realtime";
    public const string StaffGroup = "staff:portal";

    public static string UserGroup(Guid userId) => "user:" + userId.ToString("D");

    public override async Task OnConnectedAsync()
    {
        var ct = Context.ConnectionAborted;
        var user = Context.User;
        if (!Guid.TryParse(user?.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            Context.Abort();
            return;
        }

        Guid? sessionId = Guid.TryParse(user?.FindFirstValue("session_id"), out var parsed) ? parsed : null;
        if ((!AuthenticationModes.UsesBearerTokens(configuration["Authentication:Mode"])
            && !sessionId.HasValue)
            || (sessionId.HasValue
            && !await sessions.IsActive(userId, sessionId.Value, DateTimeOffset.UtcNow, ct)))
        {
            Context.Abort();
            return;
        }

        var current = await authentication.GetCurrentUser(userId, ct);
        if (current is null)
        {
            Context.Abort();
            return;
        }

        var permissions = RealtimeConnections.PermissionSignature(current.Permissions);
        if (!connections.TryAdd(Context.ConnectionId, userId, sessionId, permissions, Context.Abort))
        {
            Context.Abort();
            return;
        }

        try
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(userId), ct);
            if (current.Permissions.Contains("disputes.read") || current.Permissions.Contains("users.manage"))
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, StaffGroup, ct);
            }

            await base.OnConnectedAsync();
        }
        catch
        {
            connections.RemoveRealtimeConnection(Context.ConnectionId);
            throw;
        }
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        connections.RemoveRealtimeConnection(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}
