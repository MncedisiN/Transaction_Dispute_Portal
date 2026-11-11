using DisputePortal.Api.Configuration;
using Microsoft.AspNetCore.SignalR;
using DisputePortal.Api.Models.Domain;
using DisputePortal.Api.Interfaces.Services;

namespace DisputePortal.Api.Realtime;

public class RealtimePublisher(IHubContext<PortalRealtimeHub> hub, ILogger<RealtimePublisher> logger, ApiRuntimeOptions runtime) : IRealtimePublisher
{
    private static readonly HashSet<string> AllowedTopics = ["disputes", "support", "banking", "notifications", "profile", "administration"];

    public async Task PublishPortalUpdate(
        IReadOnlyCollection<Guid> userIds,
        bool notifyStaff,
        IReadOnlyCollection<string> topics,
        Guid? resourceId,
        CancellationToken cancellationToken)
    {
        var selected = topics.Where(AllowedTopics.Contains).Distinct(StringComparer.Ordinal).ToArray();
        if (selected.Length == 0)
        {
            return;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(runtime.RealtimePublishTimeoutSeconds));
        var change = new RealtimeChange(selected, resourceId, DateTimeOffset.UtcNow);
        try
        {
            var groups = userIds.Where(id => id != Guid.Empty).Distinct().Select(PortalRealtimeHub.UserGroup).ToArray();
            if (groups.Length > 0)
            {
                await hub.Clients.Groups(groups).SendAsync("Changed", change, timeout.Token);
            }

            if (notifyStaff)
            {
                await hub.Clients.Group(PortalRealtimeHub.StaffGroup).SendAsync(
                    "Changed",
                    new RealtimeChange(change.Topics, null, change.OccurredAt),
                    timeout.Token);
            }
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
        }
        catch (Exception error)
        { 
            logger.LogWarning("Live update delivery deferred ({FailureType}); reconnect reloads authorized state", error.GetType().Name);
        }
    }
}
