using System.Text.RegularExpressions;
using DisputePortal.Api.Interfaces.Banking;
using DisputePortal.Api.Models.Configuration;

namespace DisputePortal.Api.Service.Banking;
// A request-local immutable formatter, not a process-wide application cache.
public class TransactionPresentationFormatter(TransactionPresentationRules configured) : ICustomerTransactionPresentation
{
    private readonly Regex[] matchers = configured.DescriptionPatterns.Select(pattern => new Regex(
        pattern,
        RegexOptions.NonBacktracking,
        TimeSpan.FromMilliseconds(100))).ToArray();

    public string TypeLabel(string type, string? configuredLabel = null)
    {
        var rules = configured;
        return rules.Types.GetValueOrDefault(type) ?? configuredLabel ?? rules.FallbackLabel;
    }

    public string Description(string type, string? merchant, string? description)
    {
        var rules = configured;
        var patterns = matchers;
        if (!string.IsNullOrWhiteSpace(description))
        {
            try
            {
                if (!patterns.Any(p => p.IsMatch(description)))
                {
                    return description
                        .Replace("\u00C3\u201A\u00C2\u00B7", "\u00B7", StringComparison.Ordinal)
                        .Replace("\u00C2\u00B7", "\u00B7", StringComparison.Ordinal);
                }
            }
            catch (RegexMatchTimeoutException)
            { /* Use a safe display template; original bank data remains unchanged. */
            }
        }

        var template = string.IsNullOrWhiteSpace(merchant) ? rules.DescriptionWithoutMerchant : rules.DescriptionWithMerchant;
        return template.Replace("{typeLabel}", TypeLabel(type), StringComparison.Ordinal).Replace(
            "{merchant}",
            merchant ?? "",
            StringComparison.Ordinal);
    }
}
