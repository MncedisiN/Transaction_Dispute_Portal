using DisputePortal.Api.Interfaces.Repositories;
using DisputePortal.Api.Interfaces.Push;
using DisputePortal.Api.Interfaces.Services;

namespace DisputePortal.Api.Service.Push;

public class NotificationDeliveryService(
    INotificationDeliveryRepository repository,
    IWorkflowMessageDelivery channels,
    IPushDeliveryService push,
    IRealtimePublisher realtime,
    ILogger<NotificationDeliveryService> logger)
{
    public async Task Process(CancellationToken ct)
    {
        foreach (var item in await repository.ClaimNotifications(ct))
        {
            try
            {
                if (item.Milestone is not ("Started" or "Completed")
                    && !(item.Channel == "push"
                    && item.Milestone.StartsWith("Event:", StringComparison.Ordinal)))
                {
                    await repository.CompleteNotification(item.Id, "Skipped", "unsupported_milestone", ct);
                    continue;
                }

                // Routine lifecycle changes use push. External messages are reserved for a completed case.
                if (item.Channel != "push" && item.Milestone != "Completed")
                {
                    await repository.CompleteNotification(item.Id, "Skipped", "routine_update_uses_push", ct);
                    continue;
                }

                if (item.Channel == "push")
                {
                    var result = await push.SendPushNotification(item.UserId, item.Milestone, item.Id, ct);
                    await repository.CompleteNotification(item.Id, result.Outcome, result.Code, ct);
                }
                else
                {
                    var target = await repository.GetNotificationTarget(item.UserId, item.Channel, ct);
                    if (!target.Enabled || string.IsNullOrEmpty(target.Destination) || !channels.IsAvailable(item.Channel))
                    {
                        await repository.CompleteNotification(item.Id, "Skipped", "channel_unavailable_or_unverified", ct);
                    }
                    else
                    {
                        await channels.SendImportantWorkflowMessage(item.Channel, target.Destination, item.Milestone, ct);
                        await repository.CompleteNotification(item.Id, "Sent", null, ct);
                    }
                }

                await realtime.PublishPortalUpdate([item.UserId], false, ["notifications", "disputes", "banking"], item.DisputeId, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                await repository.CompleteNotification(item.Id, "Review", "delivery_requires_review", ct);
                logger.LogWarning("Milestone delivery {DeliveryId} requires review", item.Id);
            }
        }
    }
}
