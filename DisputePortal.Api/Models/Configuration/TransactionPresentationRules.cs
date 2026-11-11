using System.Text.RegularExpressions;

namespace DisputePortal.Api.Models.Configuration;

public class TransactionPresentationRules
{
    public string FallbackLabel { get; set; } = "";
    public Dictionary<string, string> Types { get; set; } = [];
    public string[] DescriptionPatterns { get; set; } = [];
    public string DescriptionWithMerchant { get; set; } = "";
    public string DescriptionWithoutMerchant { get; set; } = "";

    public bool IsValid()
    {
        if (string.IsNullOrWhiteSpace(FallbackLabel)
            || FallbackLabel.Length > 120
            || Types is not { Count: > 0 and <= 128 }
            || Types.Any(type => string.IsNullOrWhiteSpace(type.Key)
            || type.Key.Length > 64
            || string.IsNullOrWhiteSpace(type.Value)
            || type.Value.Length > 120)
            || DescriptionPatterns is not { Length: <= 16 }
            || DescriptionPatterns.Any(pattern => pattern is null
            || pattern.Length > 1000)
            || DescriptionWithMerchant is not { Length: > 0 and <= 1000 }
            || DescriptionWithoutMerchant is not { Length: > 0 and <= 1000 })
        {
            return false;
        }

        try
        {
            foreach (var pattern in DescriptionPatterns)
                _ = new Regex(pattern, RegexOptions.NonBacktracking, TimeSpan.FromMilliseconds(100));
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }
}
