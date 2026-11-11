using System.Collections.Frozen;
using Microsoft.Extensions.Primitives;

namespace DisputePortal.Api.Observability;

public sealed class EndpointMetricCatalog : IDisposable
{
    private readonly EndpointDataSource[] sources;
    private readonly IDisposable subscription;
    private FrozenSet<string> templates = Array.Empty<string>().ToFrozenSet(StringComparer.Ordinal);

    public EndpointMetricCatalog(IEnumerable<EndpointDataSource> sources)
    {
        this.sources = sources.ToArray();
        Refresh();
        subscription = ChangeToken.OnChange(() => new CompositeChangeToken(this.sources.Select(s => s.GetChangeToken()).ToArray()), Refresh);
    }

    private void Refresh()
    {
        var known = sources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>().Select(endpoint => endpoint.RoutePattern.RawText?.TrimStart('/')).Where(route => route is not null && route.Length <= 255).Take(2048).Cast<string>().ToFrozenSet(StringComparer.Ordinal);
        Volatile.Write(ref templates, known);
    }

    public string Normalize(string? template)
    {
        if (template is null)
        {
            return "_unmatched";
        }

        var candidate = template.TrimStart('/');
        if (Volatile.Read(ref templates).Count == 0)
        {
            Refresh();
        }

        // A framework route template may have a leading slash. Actual paths,
        // identifiers, query strings and endpoint display names are never used.
        return Volatile.Read(ref templates).Contains(candidate) ? "/" + candidate : "_unmatched";
    }

    public string GetNormalizedEndpointRoute(HttpContext context) =>
        Normalize(((context.GetEndpoint() ?? context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Endpoint) as RouteEndpoint)?.RoutePattern.RawText);

    public static string Method(string? value) =>
        value is "GET" or "POST" or "PUT" or "PATCH" or "DELETE" or "HEAD" or "OPTIONS" or "CONNECT" or "TRACE" ? value : "OTHER";

    public void Dispose() => subscription.Dispose();
}
