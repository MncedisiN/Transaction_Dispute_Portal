using DisputePortal.Api.Interfaces.Repositories;
using DisputePortal.Api.Models.Requests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DisputePortal.Api.Controllers;

[ApiController]
[Authorize(Roles = "super_admin")]
[Route("api/v1/admin/portal-settings")]
[EnableRateLimiting("operations-read")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class PortalSettingsController(IPortalSettingsRepository settings) : PortalControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetPortalSettings(CancellationToken ct) =>
        Ok(await settings.GetPortalSettings(ct));

    [HttpPost]
    [EnableRateLimiting("security")]
    public async Task<IActionResult> PublishPortalSettings(UpdatePortalSettingsRequest request, CancellationToken ct) =>
        Ok(await settings.PublishPortalSettings(UserId(User), request, ct));
}
