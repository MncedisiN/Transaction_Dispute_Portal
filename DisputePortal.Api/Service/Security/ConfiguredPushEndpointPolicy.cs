using System.Net;
using Microsoft.Extensions.Options;
using DisputePortal.Api.Models.Domain;
using DisputePortal.Api.Interfaces.Security;

namespace DisputePortal.Api.Service.Security;

public class ConfiguredPushEndpointPolicy(IOptionsMonitor<PushProviderOptions> options) : IPushEndpointPolicy
{
    public bool IsAllowed(string endpoint) => Allows(endpoint, options.CurrentValue);

    public static bool Allows(string endpoint, PushProviderOptions options)
    {
        if (endpoint.Length > 4096
            || !Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
            || uri.Scheme != "https"
            || !uri.IsDefaultPort
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Fragment)
            || uri.HostNameType != UriHostNameType.Dns)
        {
            return false;
        }

        var host = uri.IdnHost;
        return options.Providers.Take(32).Any(provider => provider.ExactHosts.Take(16).Any(allowed => ValidHost(allowed)
            && host.Equals(allowed, StringComparison.OrdinalIgnoreCase))
            || provider.SubdomainSuffixes.Take(16).Any(allowed => ValidHost(allowed)
            && allowed.Count(c => c == '.') >= 2
            && host.EndsWith("." + allowed, StringComparison.OrdinalIgnoreCase)));
    }

    private static bool ValidHost(string host) => host.Length is >= 4 and <= 253
        && Uri.CheckHostName(host) == UriHostNameType.Dns
        && host.Contains('.')
        && !host.EndsWith('.')
        && !host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
        && !IPAddress.TryParse(host, out _)
        && host.All(c => char.IsAsciiLetterOrDigit(c)
        || c is '.' or '-');
}
