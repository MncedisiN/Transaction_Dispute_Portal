using DisputePortal.Api.Models.Exceptions;
using DisputePortal.Api.Models.Requests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using DisputePortal.Api.Interfaces.Repositories;
using DisputePortal.Api.Interfaces.Accounts;

namespace DisputePortal.Api.Controllers;

[ApiController]
[Route("api/v1/account-management")]
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class AccountManagementController(IAccountManagementRepository accounts, IAccountClosureService closure) : PortalControllerBase
{
    [HttpGet("accounts/{id:guid}/number")]
    [Authorize(Policy = "customer")]
    [EnableRateLimiting("sensitive")]
    public async Task<IActionResult> Number(Guid id, CancellationToken ct) => Ok(await accounts.RevealAccountNumber(UserId(User), id, ct));

    [HttpGet("deletion")]
    [Authorize(Policy = "customer")]
    public async Task<IActionResult> Requests(CancellationToken ct) => Ok(await accounts.GetDeletionRequests(UserId(User), false, ct));

    [HttpPost("deletion/start")]
    [Authorize(Policy = "customer")]
    [EnableRateLimiting("security")]
    public async Task<IActionResult> RequestAccountClosure(StartDeletionRequest request, CancellationToken ct)
    {
        try
        {
            return Ok(await closure.StartAccountClosure(UserId(User), request, ct));
        }
        catch (PortalValidationException exception)
        {
            return ValidationFailure(exception.Field, exception.Message);
        }
    }

    [HttpPost("deletion/confirm")]
    [Authorize(Policy = "customer")]
    [EnableRateLimiting("security")]
    public async Task<IActionResult> Confirm(ConfirmDeletionRequest request, CancellationToken ct) => Ok(new
    {
        id = await accounts.ConfirmDeletionRequest(UserId(User), request, ct)
    });

    [HttpPost("deletion/{id:guid}/cancel")]
    [Authorize(Policy = "customer")]
    [EnableRateLimiting("sensitive")]
    public async Task<IActionResult> CancelAccountClosure(Guid id, CancellationToken ct)
    {
        await accounts.CancelDeletionRequest(UserId(User), id, ct);
        return NoContent();
    }

    [HttpGet("requests")]
    [Authorize(Policy = "users.manage")]
    public async Task<IActionResult> Administration(CancellationToken ct) =>
        Ok(await accounts.GetDeletionRequests(UserId(User), true, ct));

    [HttpPost("requests/{id:guid}/review")]
    [Authorize(Policy = "users.manage")]
    [EnableRateLimiting("sensitive")]
    public async Task<IActionResult> Review(Guid id, ReviewDeletionRequest request, CancellationToken ct)
    {
        await accounts.ReviewDeletionRequest(UserId(User), id, request, ct);
        return NoContent();
    }
}
