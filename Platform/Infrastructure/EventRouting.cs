namespace DisputePortal.Platform.Infrastructure;

public static class EventRouting
{
    public static void ValidateSubscriptions(ServiceOptions options)
    {
        if (!options.EventRoutes.TryGetValue(options.Name, out var configured)
            || configured is null
            || !configured.ToHashSet(StringComparer.Ordinal).SetEquals(options.Subscriptions))
        {
            throw new InvalidOperationException("Event routing configuration must match this service's registered consumers.");
        }

        if (options.EventRoutes.Keys.Any(service => !options.Endpoints.ContainsKey(service))
            || options.Endpoints.Keys.Any(service => !options.EventRoutes.ContainsKey(service))
            || options.EventRoutes.Values.Any(patterns => patterns is null
                || patterns.Any(pattern => string.IsNullOrWhiteSpace(pattern) || pattern.Length > 128)))
        {
            throw new InvalidOperationException("Configure a subscription list for every authenticated service endpoint.");
        }

        foreach (var eventType in DisputePortal.Platform.Contracts.EventTypes.Sources.Keys)
        {
            if (!options.EventRoutes.Values.Any(patterns => patterns.Any(pattern => Matches(pattern, eventType))))
            {
                throw new InvalidOperationException("Every supported event must have a configured receiver.");
            }
        }
    }

    public static string[] Receivers(ServiceOptions options, string eventType)
    {
        var targets = options.EventRoutes
            .Where(route => route.Value.Any(pattern => Matches(pattern, eventType)))
            .Select(route => route.Key)
            .ToArray();
        if (targets.Length == 0 || targets.Any(target => !options.Endpoints.ContainsKey(target)))
        {
            throw new InvalidOperationException("The event's receivers are not configured; keep it in the outbox.");
        }

        return targets;
    }

    public static bool Matches(string pattern, string eventType)
    {
        var p = pattern.Split('.');
        var e = eventType.Split('.');
        return Match(0, 0);
        bool Match(int a, int b)
        {
            if (a == p.Length)
            {
                return b == e.Length;
            }

            if (p[a] == "#")
            {
                for (var i = b; i <= e.Length; i++)
                    if (Match(a + 1, i))
                    {
                        return true;
                    }

                return false;
            }

            return b < e.Length && (p[a] == "*" || p[a] == e[b]) && Match(a + 1, b + 1);
        }
    }
}
