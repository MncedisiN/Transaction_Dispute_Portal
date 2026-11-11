namespace DisputePortal.Api.Interfaces.Security;

public interface IEvidenceScanner
{
    Task<string> Scan(byte[] content, CancellationToken ct);
}
