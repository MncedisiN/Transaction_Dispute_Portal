using System.Collections.Concurrent;

namespace DisputePortal.Api.Realtime;

public class RealtimeConnections
{
    private readonly ConcurrentDictionary<string, RealtimeConnection> connections = new();
    private readonly object gate = new();

    public IReadOnlyList<RealtimeConnection> Snapshot() => connections.Values.ToArray();

    public static string PermissionSignature(IEnumerable<string> permissions) =>
        string.Join('|', permissions.Order(StringComparer.Ordinal));

    public bool TryAdd(string id, Guid user, Guid? session, string permissions, Action abort)
    {
        lock (gate)
        {
            if (connections.Count >= 512 || connections.Values.Count(connection => connection.UserId == user) >= 4)
            {
                return false;
            }

            return connections.TryAdd(
                id,
                new global::DisputePortal.Api.Realtime.RealtimeConnection
                {
                    ConnectionId = id,
                    UserId = user,
                    SessionId = session,
                    Permissions = permissions,
                    Abort = abort
                });
        }
    }

    public void RemoveRealtimeConnection(string id) => connections.TryRemove(id, out _);
}
