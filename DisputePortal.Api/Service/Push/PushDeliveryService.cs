using DisputePortal.Api.Models.Internal;
using System.Text.Json;
using DisputePortal.Api.Service.ExternalServices;
using DisputePortal.Api.Configuration;
using DisputePortal.Api.Service.Security;
using DisputePortal.Api.Interfaces.Repositories;
using DisputePortal.Api.Interfaces.ExternalServices;
using DisputePortal.Api.Models.Domain;
using DisputePortal.Api.Interfaces.Push;
using DisputePortal.Api.Interfaces.Security;

namespace DisputePortal.Api.Service.Push;

public class PushDeliveryService(
    INotificationDeliveryRepository repository,
    IPushTransport transport,
    PushOptions options,
    IPushEndpointPolicy? endpointPolicy = null,
    IMessageTemplates? templates = null) : IPushDeliveryService
{
    public async Task<PushDeliveryResult> SendPushNotification(
        Guid user,
        string milestone,
        Guid deliveryId,
        CancellationToken ct,
        Guid? session = null)
    {
        if (!options.IsConfigured)
        {
            return new("Skipped", "push_not_configured", 0, 0);
        }

        var subscriptions = await repository.GetPushSubscriptions(user, ct, session);
        if (subscriptions.Count == 0)
        {
            return new("Skipped", "no_active_subscription", 0, 0);
        }

        var context = await repository.GetPushContext(user, deliveryId, ct);
        if (context is null && milestone.StartsWith("Event:", StringComparison.Ordinal))
        {
            return new("Skipped", "recipient_scope_changed", 0, 0);
        }

        var unread = context?.UnreadCount ?? await repository.GetUnreadNotificationCount(user, ct);
        var payload = CreatePayload(context?.Kind ?? milestone, deliveryId, unread, context?.Url, templates, context?.Body);
        var sent = 0;
        var unavailable = 0;
        var review = 0;
        foreach (var subscription in subscriptions)
        {
            try
            {
                var result = (endpointPolicy?.IsAllowed(subscription.Endpoint) ?? PushEndpointPolicy.IsAllowed(subscription.Endpoint)) ? await transport.SendEncryptedWebPush(
                    subscription,
                    payload,
                    ct) : PushAttempt.Gone;
                switch (result)
                {
                    case PushAttempt.Accepted:
                        sent++;
                        break;
                    case PushAttempt.Gone:
                        await repository.DeletePushSubscription(subscription.Id, ct);
                        unavailable++;
                        break;
                    default:
                        review++;
                        break;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                review++;
            }
        }

        return review > 0 ? new("Review", sent > 0 ? "partial_delivery_requires_review" : "delivery_requires_review", sent, unavailable) : sent > 0 ? new(
            "Sent",
            null,
            sent,
            unavailable) : new("Skipped", "no_active_subscription", 0, unavailable);
    }

    public static string CreatePayload(
        string milestone,
        Guid deliveryId,
        long unreadCount = 0,
        string? url = null,
        IMessageTemplates? templates = null,
        string? eventBody = null)
    {
        var key = milestone.Replace("Event:", "", StringComparison.Ordinal);
        if (!System.Text.RegularExpressions.Regex.IsMatch(key, "^[A-Za-z]+$", System.Text.RegularExpressions.RegexOptions.NonBacktracking))
        {
            key = "Default";
        }

        var message = (templates ?? MessageTemplateCatalog.Default).Render("push/" + key, new Dictionary<string, string>());
        return JsonSerializer.Serialize(new
        {
            title = message.Subject,
            body = key is "TransferCompleted" or "TransferFailed" or "RefundRecalled" or "RefundReviewCompleted" or "RefundReviewFailed" or "RefundReviewIneligible"
            && eventBody is { Length: > 0 and <= 500 } ? eventBody : message.Text,
            url = url is not null && url.StartsWith('/') && !url.StartsWith("//") && !url.Contains('\\') ? url : "/notifications",
            tag = deliveryId.ToString("N"),
            unreadCount = Math.Max(0, unreadCount)
        });
    }
}
