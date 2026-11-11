using DisputePortal.Api.Repository;
using DisputePortal.Platform.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DisputePortal.Api.Controllers;

[ApiController]
[Route("api/internal/country-policy")]
[Authorize(AuthenticationSchemes = ServiceSecurity.InternalServiceScheme)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class CountryPolicyController(CountryPolicyEligibilityReader eligibility) : ControllerBase
{
    [HttpGet("eligibility")]
    public async Task<IActionResult> Eligibility(CancellationToken ct)
    {
        User.RequireCaller("configuration");
        return Ok(await eligibility.GetCountryChangeBlockers(ct));
    }
}
