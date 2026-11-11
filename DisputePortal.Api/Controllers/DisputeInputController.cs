using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using DisputePortal.Api.Interfaces.Repository;

namespace DisputePortal.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/dispute-input")]
[EnableRateLimiting("operations-read")]
public class DisputeInputController(IDisputeInputPolicyStore policies) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetDisputeInputPolicy(CancellationToken ct) => Ok(await policies.GetDisputeInputPolicy(ct));
}
