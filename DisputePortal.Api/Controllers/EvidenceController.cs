using DisputePortal.Api.Models.Domain;
using DisputePortal.Api.Models.Exceptions;
using DisputePortal.Api.Models.Requests;
using DisputePortal.Api.Models.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using DisputePortal.Api.Interfaces.Repositories;
using DisputePortal.Api.Interfaces.Security;

namespace DisputePortal.Api.Controllers;

[ApiController]
[Route("api/v1/disputes/{disputeId:guid}/evidence")]
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class EvidenceController(IEvidenceRepository evidence, IEvidenceScanner scanner, ILogger<EvidenceController>? logger = null) : PortalControllerBase
{
    private static readonly SemaphoreSlim UploadSlots = new(2, 2);

    [HttpGet]
    public async Task<IActionResult> GetCaseEvidence(Guid disputeId, CancellationToken ct) => Ok(await evidence.GetEvidence(
        UserId(User),
        disputeId,
        ct));

    [HttpPost]
    [Consumes("multipart/form-data")]
    [EnableRateLimiting("sensitive")]
    [ServiceFilter(typeof(DisputePortal.Api.Middleware.EvidenceUploadLimitsFilter))]
    public async Task<IActionResult> Upload(
        Guid disputeId,
        IFormFile file,
        [FromForm] string category,
        CancellationToken ct,
        IFormFile? original = null)
    {
        var policy = HttpContext.Items[typeof(DisputePortal.Api.Models.Configuration.EvidencePolicy)]
            as DisputePortal.Api.Models.Configuration.EvidencePolicy
            ?? throw new InvalidOperationException("Evidence upload policy was not loaded before form binding.");
        if (!policy.Categories.Any(option => option.Code == category))
        {
            throw new PortalValidationException("Choose a document category.");
        }

        await evidence.EnsureEvidenceUploadAllowed(UserId(User), disputeId, ct);
        if (file is null || file.Length <= 0 || file.Length > policy.MaximumFileBytes)
        {
            throw new PortalValidationException("Choose a file within the configured evidence upload limit.");
        }

        await UploadSlots.WaitAsync(ct);
        try
        {
            var bytes = await ReadBounded(file, policy.MaximumFileBytes, ct);
            try
            {
                EvidenceFilePolicy.Inspect(file.FileName, bytes, policy);
            }
            catch (ArgumentException e)
            {
                throw new PortalValidationException(e.Message);
            }

            var status = await scanner.Scan(bytes, ct);
            if (status != "Validated")
            {
                throw new EvidenceScanUnavailableException();
            }

            EvidenceOriginal? retained = null;
            if (original is not null)
            {
                var source = await ReadBounded(original, policy.MaximumOriginalBytes, ct);
                EvidenceFileDescription sourceInfo;
                try
                {
                    sourceInfo = EvidenceFilePolicy.Inspect(original.FileName, source, policy, originalFile: true);
                }
                catch (ArgumentException e)
                {
                    throw new PortalValidationException(e.Message);
                }

                if (!sourceInfo.ContentType.StartsWith("image/", StringComparison.Ordinal))
                {
                    throw new PortalValidationException("The original must be a supported image.");
                }

                var originalScan = await scanner.Scan(source, ct);
                if (originalScan != status)
                {
                    throw new EvidenceScanUnavailableException();
                }

                retained = new(original.FileName, source);
            }

            logger?.LogInformation(
                "Evidence intake validated {DisputeId} {PrimaryBytes} {OriginalBytes}",
                disputeId,
                bytes.Length,
                retained?.Content.Length ?? 0);
            var id = await evidence.AddEvidence(UserId(User), disputeId, file.FileName, bytes, status, category, ct, retained);
            return Created($"/api/v1/disputes/{disputeId}/evidence/{id}", new { id });
        }
        finally
        {
            UploadSlots.Release();
        }
    }

    [HttpPost("{id:guid}/remove")]
    [EnableRateLimiting("sensitive")]
    public async Task<IActionResult> SoftDeleteCaseEvidence(Guid disputeId, Guid id, RemoveEvidenceRequest request, CancellationToken ct)
    {
        await evidence.RemoveEvidence(UserId(User), disputeId, id, request, ct);
        return NoContent();
    }

    private static async Task<byte[]> ReadBounded(IFormFile file, int limit, CancellationToken ct)
    {
        if (file.Length <= 0 || file.Length > limit)
        {
            throw new PortalValidationException("This file exceeds the supported upload limit.");
        }

        await using var source = file.OpenReadStream();
        using var destination = new MemoryStream();
        var chunk = new byte[65536];
        int read;
        while ((read = await source.ReadAsync(chunk, ct)) > 0)
        {
            if (destination.Length + read > limit)
            {
                throw new PortalValidationException("This file exceeds the supported upload limit.");
            }

            await destination.WriteAsync(chunk.AsMemory(0, read), ct);
        }

        return destination.ToArray();
    }

    [HttpGet("{id:guid}/original")]
    [EnableRateLimiting("sensitive")]
    public async Task<IActionResult> Original(Guid disputeId, Guid id, CancellationToken ct)
    {
        var file = await evidence.DownloadOriginalEvidence(UserId(User), disputeId, id, ct);
        var verdict = await scanner.Scan(file.Content, ct);
        if (verdict != "Validated")
        {
            throw new EvidenceScanUnavailableException();
        }

        Response.Headers.ContentSecurityPolicy = "sandbox; default-src 'none'";
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(file.Content, "application/octet-stream", file.FileName, enableRangeProcessing: false);
    }

    [HttpGet("{id:guid}/access")]
    public async Task<IActionResult> Access(Guid disputeId, Guid id, CancellationToken ct) => Ok(await evidence.GetEvidenceAccess(
        UserId(User),
        disputeId,
        id,
        ct));

    [HttpPost("{id:guid}/access")]
    [EnableRateLimiting("sensitive")]
    public async Task<IActionResult> Grant(Guid disputeId, Guid id, GrantEvidenceAccessRequest request, CancellationToken ct)
    {
        await evidence.GrantEvidenceAccess(UserId(User), disputeId, id, request, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/access/{grantId:guid}/revoke")]
    [EnableRateLimiting("sensitive")]
    public async Task<IActionResult> Revoke(
        Guid disputeId,
        Guid id,
        Guid grantId,
        RevokeEvidenceAccessRequest request,
        CancellationToken ct)
    {
        await evidence.RevokeEvidenceAccess(UserId(User), disputeId, id, grantId, request, ct);
        return NoContent();
    }

    [HttpGet("{id:guid}/preview")]
    [EnableRateLimiting("sensitive")]
    public async Task<IActionResult> Preview(Guid disputeId, Guid id, CancellationToken ct)
    {
        var file = await evidence.PreviewEvidence(UserId(User), disputeId, id, ct);
        var verdict = await scanner.Scan(file.Content, ct);
        if (verdict != "Validated")
        {
            throw new EvidenceScanUnavailableException();
        }

        if (file.ContentType is not ("image/png" or "image/jpeg" or "image/webp" or "application/pdf"))
        {
            throw new PortalValidationException("This file cannot be previewed.");
        }

        Response.Headers.ContentSecurityPolicy = "sandbox; default-src 'none'";
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(file.Content, file.ContentType, enableRangeProcessing: false);
    }

    [HttpGet("{id:guid}")]
    [EnableRateLimiting("sensitive")]
    public async Task<IActionResult> Download(Guid disputeId, Guid id, CancellationToken ct)
    {
        var file = await evidence.DownloadEvidence(UserId(User), disputeId, id, ct); 
        var verdict = await scanner.Scan(file.Content, ct);
        if (verdict != "Validated")
        {
            throw new EvidenceScanUnavailableException();
        }

        Response.Headers.ContentSecurityPolicy = "sandbox; default-src 'none'";
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(file.Content, "application/octet-stream", file.FileName, enableRangeProcessing: false);
    }
}
