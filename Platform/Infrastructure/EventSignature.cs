using System.Security.Cryptography;
using System.Text.Json;
using DisputePortal.Platform.Contracts;

namespace DisputePortal.Platform.Infrastructure;

public static class EventSignature
{
    public static DomainEvent Verify(ReadOnlySpan<byte> body, string? signature, ServiceOptions options)
    {
        if (body.Length > options.MaximumEventBytes || string.IsNullOrWhiteSpace(signature) || signature.Length > 256)
        {
            throw new InvalidOperationException("Invalid event size or signature.");
        }

        var value = JsonSerializer.Deserialize<DomainEvent>(body, DomainEvent.Json) ?? throw new InvalidOperationException("Invalid event envelope.");
        value.ValidateEventEnvelope();
        if (!options.PublicKeys.TryGetValue(value.SourceService, out var key))
        {
            throw new InvalidOperationException("Unknown event producer.");
        }

        using var verifier = ECDsa.Create();
        verifier.ImportFromPem(key);
        if (!verifier.VerifyData(body, Convert.FromBase64String(signature), HashAlgorithmName.SHA256))
        {
            throw new InvalidOperationException("Invalid producer signature.");
        }

        return value;
    }
}
