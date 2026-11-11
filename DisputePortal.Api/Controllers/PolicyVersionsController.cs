using DisputePortal.Api.Models.Requests;
using DisputePortal.Api.Repository;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DisputePortal.Api.Controllers;

[ApiController, Authorize(Roles = "super_admin"), Route("api/v1/admin/policies"), EnableRateLimiting("operations-read")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class PolicyVersionsController(PolicyVersionRepository policies) : PortalControllerBase
{
    [HttpGet("automatic/history")]
    public async Task<IActionResult> AutomaticHistory(
        int page = 1,
        int pageSize = 20,
        string? search = null,
        CancellationToken ct = default) => Ok(await policies.AutomaticHistory(UserId(User), page, pageSize, search, ct));

    [HttpGet("routing/history")]
    public async Task<IActionResult> RoutingHistory(int page = 1, int pageSize = 20, string? search = null, CancellationToken ct = default) =>
        Ok(await policies.RoutingHistory(UserId(User), page, pageSize, search, ct));

    [HttpPost("automatic/preview"), EnableRateLimiting("security")]
    public async Task<IActionResult> PreviewAutomatic(UpdateAutomaticRefundPolicyRequest request, CancellationToken ct) =>
        Ok(await policies.PreviewAutomatic(UserId(User), request, ct));

    [HttpPost("routing/preview"), EnableRateLimiting("security")]
    public async Task<IActionResult> PreviewRouting(RiskPolicyUpdateRequest request, CancellationToken ct) =>
        Ok(await policies.PreviewRouting(UserId(User), request, ct));

    [HttpPost("automatic/versions"), EnableRateLimiting("security")]
    public async Task<IActionResult> PublishAutomatic(UpdateAutomaticRefundPolicyRequest request, CancellationToken ct)
    {
        await policies.PublishAutomatic(UserId(User), request, ct);
        return NoContent();
    }

    [HttpPost("routing/versions"), EnableRateLimiting("security")]
    public async Task<IActionResult> PublishRouting(RiskPolicyUpdateRequest request, CancellationToken ct)
    {
        await policies.PublishRouting(UserId(User), request, ct);
        return NoContent();
    }

    [HttpPost("{kind}/{version:guid}/removal-preview"), EnableRateLimiting("security")]
    public async Task<IActionResult> RemovalPreview(string kind, Guid version, DeletePolicyVersionRequest request, CancellationToken ct) =>
        Ok(await policies.SoftDeletePolicyVersion(UserId(User), Automatic(kind), version, request, true, ct));

    [HttpPost("{kind}/{version:guid}/remove"), EnableRateLimiting("security")]
    public async Task<IActionResult> SoftDeletePolicyVersion(
        string kind,
        Guid version,
        DeletePolicyVersionRequest request,
        CancellationToken ct) => Ok(await policies.SoftDeletePolicyVersion(UserId(User), Automatic(kind), version, request, false, ct));

    private static bool Automatic(string kind) => kind switch
    {
        "automatic" => true,
        "routing" => false,
        _ => throw new Models.Exceptions.PortalNotFoundException()
    };
}
