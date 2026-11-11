using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DisputePortal.Api.Controllers;

[ApiController]
[Route("api/v1/banking-provisioning")]
[Authorize(Policy = "customer")]
public class BankingProvisioningController : PortalControllerBase
{
    [HttpGet]
    public IActionResult Status() => Ok(new { enabled = false });
}
