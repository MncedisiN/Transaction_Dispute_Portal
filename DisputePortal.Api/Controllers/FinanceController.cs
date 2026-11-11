using DisputePortal.Api.Models.Requests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using DisputePortal.Api.Interfaces.Repositories;

namespace DisputePortal.Api.Controllers;

[ApiController]
[Route("api/v1/finance")]
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class FinanceController(IFinanceRepository finance) : PortalControllerBase
{
    [HttpGet("overview")]
    [Authorize(Policy = "customer")]
    public async Task<IActionResult> Overview(CancellationToken ct) => Ok(await finance.GetOverview(UserId(User), ct));

    [HttpGet("balances")]
    [Authorize(Policy = "customer")]
    public async Task<IActionResult> Balances(CancellationToken ct) => Ok(await finance.GetBalances(UserId(User), ct));

    [HttpGet("accounts/{id:guid}/history")]
    [Authorize(Policy = "customer")]
    public async Task<IActionResult> History(Guid id, CancellationToken ct) => Ok(await finance.GetBalanceHistory(UserId(User), id, ct));

    [HttpGet("disputes/{id:guid}")]
    public async Task<IActionResult> Summary(Guid id, CancellationToken ct) => Ok(await finance.GetCaseFinancials(UserId(User), id, ct));

    [HttpGet("disputes/{id:guid}/activity")]
    public async Task<IActionResult> Activity(Guid id, CancellationToken ct) => Ok(await finance.GetCaseActivity(UserId(User), id, ct));

    [HttpPost("disputes/{id:guid}/refund")]
    [Authorize(Policy = "refund.approve")]
    [EnableRateLimiting("security")]
    public async Task<IActionResult> Refund(Guid id, ApproveRefundRequest request, CancellationToken ct)
    {
        var correlation = HttpContext.Items["CorrelationId"]?.ToString() ?? "unknown";
        var result = await finance.ApproveRefund(UserId(User), id, request, correlation, ct);
        return result.Status == "Processing" ? Accepted(result) : Ok(result);
    }

    [HttpPost("disputes/{id:guid}/recall")]
    [Authorize(Policy = "disputes.manage")]
    [EnableRateLimiting("security")]
    public async Task<IActionResult> RecallDisputeRefund(Guid id, RecallRefundRequest request, CancellationToken ct)
    {
        var correlation = HttpContext.Items["CorrelationId"]?.ToString() ?? "unknown";
        var result = await finance.RecallRefund(UserId(User), id, request, correlation, ct);
        return result.Status == "Processing" ? Accepted(result) : Ok(result);
    }

    [HttpGet("automatic-policy")]
    [Authorize(Policy = "operations.manage")]
    public async Task<IActionResult> Policy(CancellationToken ct) => Ok(await finance.GetPolicy(UserId(User), ct));

    [HttpPost("automatic-policy")]
    [Authorize(Policy = "operations.manage")]
    [EnableRateLimiting("security")]
    public async Task<IActionResult> UpdatePolicy(UpdateAutomaticRefundPolicyRequest request, CancellationToken ct)
    {
        await finance.UpdatePolicy(UserId(User), request, ct);
        return NoContent();
    }
}
