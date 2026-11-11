using DisputePortal.Api.Models.Domain;
using DisputePortal.Api.Models.Exceptions;
using DisputePortal.Api.Service.ExternalServices;
using DisputePortal.Api.Interfaces.Security;

namespace DisputePortal.Api.Service.Security;
/// <summary>In-process format and image validation; does not perform antivirus detection.</summary>
public class ApiEvidenceScanner : IEvidenceScanner
{
    public async Task<string> Scan(byte[] content, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // Stored evidence remains readable after an upload format or size limit is changed.
        string contentType;
        try
        {
            contentType = EvidenceFilePolicy.DetectContentType(content);
        }
        catch (ArgumentException e)
        {
            throw new PortalValidationException(e.Message);
        }

        await EvidenceImageValidator.ValidateEvidenceImage(content, contentType, ct);
        ct.ThrowIfCancellationRequested();
        return "Validated";
    }
}
