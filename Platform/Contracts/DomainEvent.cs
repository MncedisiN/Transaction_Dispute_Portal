using System.Text.Json;

namespace DisputePortal.Platform.Contracts;

public class DomainEvent
{
    public Guid EventId { get; set; }
    public string EventType { get; set; } = null!;
    public int EventVersion { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public Guid CorrelationId { get; set; }
    public Guid? CausationId { get; set; }
    public string? TransactionReference { get; set; }
    public string SourceService { get; set; } = null!;
    public JsonElement Payload { get; set; }

    public static DomainEvent CreateDomainEvent<T>(
        string source,
        string type,
        T payload,
        Guid? correlation = null,
        Guid? cause = null,
        string? reference = null)
    {
        var value = new DomainEvent
        {
            EventId = Guid.NewGuid(),
            EventType = type,
            EventVersion = 1,
            Timestamp = DateTimeOffset.UtcNow,
            CorrelationId = correlation ?? Guid.NewGuid(),
            CausationId = cause,
            TransactionReference = reference,
            SourceService = source,
            Payload = JsonSerializer.SerializeToElement(payload, Json)
        };
        value.ValidateEventEnvelope();
        return value;
    }

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };

    public T DeserializePayload<T>() => Payload.Deserialize<T>(Json) ?? throw new InvalidOperationException("Missing event payload.");

    public void ValidateEventEnvelope()
    {
        if (EventId == Guid.Empty
            || CorrelationId == Guid.Empty
            || EventVersion != 1
            || !EventTypes.Sources.TryGetValue(EventType, out var owner)
            || owner != SourceService
            || Timestamp.Offset != TimeSpan.Zero
            || Timestamp > DateTimeOffset.UtcNow.AddMinutes(1)
            || TransactionReference?.Length > 100)
        {
            throw new InvalidOperationException("Invalid event envelope.");
        }

        RejectSensitivePayloadFields(Payload);
    }

    private static void RejectSensitivePayloadFields(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in value.EnumerateObject())
            {
                if (new[]
                {
                    "password",
                    "passwordhash",
                    "token",
                    "accesstoken",
                    "refreshtoken",
                    "email",
                    "phone",
                    "identitynumber",
                    "documentnumber",
                    "address",
                    "apikey",
                    "destination",
                    "contact"
                }.Contains(property.Name.Replace("_", "").ToLowerInvariant()))
                {
                    throw new InvalidOperationException("Sensitive fields cannot be published.");
                }

                RejectSensitivePayloadFields(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
                RejectSensitivePayloadFields(item);
        }
    }

    public DomainEvent()
    {
    }

    public DomainEvent(DomainEvent source)
    {
        EventId = source.EventId;
        EventType = source.EventType;
        EventVersion = source.EventVersion;
        Timestamp = source.Timestamp;
        CorrelationId = source.CorrelationId;
        CausationId = source.CausationId;
        TransactionReference = source.TransactionReference;
        SourceService = source.SourceService;
        Payload = source.Payload;
    }
}
