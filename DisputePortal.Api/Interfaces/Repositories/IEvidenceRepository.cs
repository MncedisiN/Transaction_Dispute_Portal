using DisputePortal.Api.Models.Requests;
using DisputePortal.Api.Models.Responses;

namespace DisputePortal.Api.Interfaces.Repositories;

public interface IEvidenceRepository
{
    Task<IReadOnlyList<EvidenceDto>> GetEvidence(Guid actor, Guid disputeId, CancellationToken ct);

    Task RemoveEvidence(Guid actor, Guid disputeId, Guid evidenceId, RemoveEvidenceRequest request, CancellationToken ct);

    Task EnsureEvidenceUploadAllowed(Guid actor, Guid disputeId, CancellationToken ct);

    Task<Guid> AddEvidence(
        Guid actor,
        Guid disputeId,
        string fileName,
        byte[] content,
        string scanStatus,
        string category,
        CancellationToken ct,
        EvidenceOriginal? original = null);

    Task<EvidenceDownload> DownloadEvidence(Guid actor, Guid disputeId, Guid evidenceId, CancellationToken ct);

    Task<EvidenceDownload> DownloadOriginalEvidence(Guid actor, Guid disputeId, Guid evidenceId, CancellationToken ct);

    Task<EvidenceDownload> PreviewEvidence(Guid actor, Guid disputeId, Guid evidenceId, CancellationToken ct);

    Task<EvidenceAccessDto> GetEvidenceAccess(Guid actor, Guid disputeId, Guid evidenceId, CancellationToken ct);

    Task GrantEvidenceAccess(Guid actor, Guid disputeId, Guid evidenceId, GrantEvidenceAccessRequest request, CancellationToken ct);

    Task RevokeEvidenceAccess(
        Guid actor,
        Guid disputeId,
        Guid evidenceId,
        Guid grantId,
        RevokeEvidenceAccessRequest request,
        CancellationToken ct);
}
