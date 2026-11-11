using System.Diagnostics;
using System.Text;

namespace DisputePortal.Platform.Infrastructure;

public static class EventTraceContext
{
    public static ActivityContext Read(IDictionary<string, object?>? headers)
    {
        if (headers is null || !headers.TryGetValue("traceparent", out var parent))
        {
            return default;
        }

        headers.TryGetValue("tracestate", out var state);
        return ActivityContext.TryParse(Text(parent, 128), Text(state, 512), true, out var context) ? context : default;
    }

    public static void Write(IDictionary<string, object?> headers)
    {
        if (Activity.Current is { IdFormat: ActivityIdFormat.W3C, Id: { } parent } activity)
        {
            headers["traceparent"] = parent;
            if (activity.TraceStateString is { } state)
            {
                headers["tracestate"] = state;
            }
        }
    }

    private static string? Text(object? value, int maximumLength) => value switch
    {
        string text when text.Length <= maximumLength => text,
        byte[] bytes when bytes.Length <= maximumLength => Encoding.ASCII.GetString(bytes),
        _ => null
    };
}
