namespace DisputePortal.Api.Observability;

public class MetricKey : IEquatable<MetricKey>
{
    public DateTimeOffset BucketStart { get; set; }
    public string Method { get; set; } = null!;
    public string Route { get; set; } = null!;
    public int StatusCode { get; set; }

    public bool Equals(MetricKey? other) => other is not null
        && BucketStart == other.BucketStart
        && Method == other.Method
        && Route == other.Route
        && StatusCode == other.StatusCode;

    public override bool Equals(object? value) => value is MetricKey other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(BucketStart, Method, Route, StatusCode);
}
