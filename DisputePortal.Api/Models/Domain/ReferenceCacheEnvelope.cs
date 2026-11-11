namespace DisputePortal.Api.Models.Domain;

public class ReferenceCacheEnvelope<T>
{
    public int RepresentationVersion { get; set; } = 1;
    public T? Value { get; set; }
}
