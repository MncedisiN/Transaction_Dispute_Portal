using System.Diagnostics;
using OpenTelemetry;

namespace DisputePortal.Platform.Infrastructure;
 
public sealed class TracePrivacyProcessor : BaseProcessor<Activity>
{
    private static readonly HashSet<string> AllowedTags =
    [
        "http.request.method", "http.response.status_code", "http.route", "error.type",
        "messaging.system", "messaging.operation.name", "messaging.message.type",
        "portal.correlation_id", "correlation.id", "telemetry.summary.count"
    ];

    public override void OnEnd(Activity activity)
    {
        foreach (var tag in activity.TagObjects.ToArray())
        {
            if (!AllowedTags.Contains(tag.Key))
            {
                activity.SetTag(tag.Key, null);
            }
        }

        if (activity.GetTagItem("http.request.method") is string method)
        {
            var safeMethod = method is "GET" or "POST" or "PUT" or "PATCH" or "DELETE" or "HEAD" or "OPTIONS" ? method : "HTTP";
             
            var route = activity.GetTagItem("http.route") as string;
            activity.DisplayName = route is { Length: > 0 and <= 256 } ? safeMethod + " " + route : safeMethod;
        }
         
        activity.SetStatus(activity.Status);
    }
}
