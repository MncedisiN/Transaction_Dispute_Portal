using DisputePortal.Api.Models.Requests;
using DisputePortal.Api.Repository;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DisputePortal.Api.Controllers;

[ApiController, Route("api/v1/lookups/case-statuses"), EnableRateLimiting("operations-read")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class CaseStatusesController(CaseStatusRepository statuses) : PortalControllerBase
{
    [HttpGet, AllowAnonymous]
    public async Task<IActionResult> GetWorkflowStatuses(CancellationToken ct) => Ok(await statuses.GetWorkflowStatuses(ct));

    [HttpPost("{code}"), Authorize(Roles = "super_admin"), EnableRateLimiting("security")]
    public async Task<IActionResult> UpdateWorkflowStatus(string code, UpdateCaseStatusRequest request, CancellationToken ct)
    {
        await statuses.UpdateWorkflowStatus(UserId(User), code, request, ct);
        return NoContent();
    }
}
