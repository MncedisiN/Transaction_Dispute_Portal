using System.Security.Cryptography;
using System.Text.Json;
using DisputePortal.Api.Caching;
using DisputePortal.Api.Interfaces.Banking;
using DisputePortal.Api.Models.Configuration;

namespace DisputePortal.Api.Service.Banking;

public class CustomerTransactionPresentation(string file, PortalReferenceCache? cache = null) : ICustomerTransactionPresentation
{
    public static ICustomerTransactionPresentation Default => new CustomerTransactionPresentation(Path.Combine(
        AppContext.BaseDirectory,
        "Templates",
        "Transactions",
        "presentation.json"));

    private static TransactionPresentationRules ParsePresentationRules(byte[] bytes)
    {
        var value = JsonSerializer.Deserialize<TransactionPresentationRules>(bytes, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        if (value is null || !value.IsValid())
        {
            throw new InvalidOperationException("Transaction presentation configuration is invalid.");
        }

        return value;
    }

    public async Task<ICustomerTransactionPresentation> LoadTransactionPresentationRules(CancellationToken ct)
    {
        if (new FileInfo(file).Length > 131072)
        {
            throw new InvalidOperationException("Transaction presentation configuration is too large.");
        }

        // The file remains authoritative and live-reloadable. Its content fingerprint,
        // not a mutable 'current' Redis key, separates deployed/reloaded representations.
        var bytes = await File.ReadAllBytesAsync(file, ct);
        if (bytes.Length > 131072)
        {
            throw new InvalidOperationException("Transaction presentation configuration is too large.");
        }

        var revision = Convert.ToHexString(SHA256.HashData(bytes));
        var rules = cache is null ? ParsePresentationRules(bytes) : await cache.GetReferenceData(
            "transaction-presentation",
            "packaged-presentation-rules",
            revision,
            _ => Task.FromResult(ParsePresentationRules(bytes)),
            value => value.IsValid(),
            ct);
        return new TransactionPresentationFormatter(rules);
    }

    // Compatibility for standalone callers; no local data is memoised. HTTP readers
    // prepare one formatter asynchronously and reuse only that operation's local value.
    private TransactionPresentationFormatter Standalone()
    {
        if (new FileInfo(file).Length > 131072)
        {
            throw new InvalidOperationException("Transaction presentation configuration is too large.");
        }

        return new(ParsePresentationRules(File.ReadAllBytes(file)));
    }

    public string TypeLabel(string type, string? configuredLabel = null) => Standalone().TypeLabel(type, configuredLabel);

    public string Description(string type, string? merchant, string? description) => Standalone().Description(type, merchant, description);
}
