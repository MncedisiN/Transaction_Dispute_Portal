using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace DisputePortal.Platform.Infrastructure;

public static class PlatformMetrics
{
    public const string InstrumentationName = "DisputePortal.Platform"; 
    private static readonly Meter Meter = new(InstrumentationName);
    public static readonly ActivitySource Activities = new(InstrumentationName);
    private static readonly Histogram<double> Duration = Meter.CreateHistogram<double>("portal.event.processing.duration", "s");
    private static long published;
    private static long consumed;
    private static long failures;

    static PlatformMetrics()
    {
        Meter.CreateObservableCounter("portal.events.published", () => Interlocked.Read(ref published));
        Meter.CreateObservableCounter("portal.events.consumed", () => Interlocked.Read(ref consumed));
        Meter.CreateObservableCounter("portal.events.failed", () => Interlocked.Read(ref failures));
    }

    public static void RecordPublished() => Interlocked.Increment(ref published);
    public static void RecordConsumed() => Interlocked.Increment(ref consumed);
    public static void RecordFailure() => Interlocked.Increment(ref failures);

    public static Activity? StartEventActivity(string operation, DisputePortal.Platform.Contracts.DomainEvent value, ActivityKind kind, ActivityContext parent = default)
    {
        var activity = Activities.StartActivity(operation, kind, parent);
        activity?.SetTag("messaging.system", "dispute-portal");
        activity?.SetTag("messaging.operation.name", operation);
        activity?.SetTag("messaging.message.type", value.EventType);
        activity?.SetTag("portal.correlation_id", value.CorrelationId.ToString("D"));
        return activity;
    }

    public static void RecordDuration(long started, string eventType, bool successful)
    { 
        var knownType = DisputePortal.Platform.Contracts.EventTypes.Sources.ContainsKey(eventType) ? eventType : "other";
        Duration.Record(Stopwatch.GetElapsedTime(started).TotalSeconds,
            new KeyValuePair<string, object?>("messaging.message.type", knownType),
            new KeyValuePair<string, object?>("outcome", successful ? "success" : "failure"));
    }

    public static string Render() =>
        $"platform_events_published_total {Interlocked.Read(ref published)}\nplatform_events_consumed_total {Interlocked.Read(ref consumed)}\nplatform_events_failed_total {Interlocked.Read(ref failures)}\n";
}
