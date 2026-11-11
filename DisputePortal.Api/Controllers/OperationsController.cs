using DisputePortal.Api.Models;
using DisputePortal.Api.Models.Requests;
using DisputePortal.Api.Service.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using DisputePortal.Api.Interfaces.Repositories;

namespace DisputePortal.Api.Controllers;

[ApiController]
[Route("api/v1/operations")]
[Authorize]
[EnableRateLimiting("sensitive")]
public class OperationsController(
    IOperationsRepository operations,
    IUserAdministrationRepository users,
    IPasswordHasher<PortalPasswordUser> hasher,
    PasswordSafety? safety = null) : PortalControllerBase
{
    [HttpGet("users")]
    [EnableRateLimiting("operations-read")]
    [Authorize(Policy = "users.manage")]
    public async Task<IActionResult> Users(CancellationToken ct) => Ok(await users.GetStaff(UserId(User), true, ct));

    [HttpGet("staff")]
    [EnableRateLimiting("operations-read")]
    [Authorize(Policy = "disputes.read")]
    public async Task<IActionResult> Staff(CancellationToken ct) => Ok(await users.GetStaff(UserId(User), false, ct));

    [HttpPost("users")]
    [Authorize(Policy = "users.manage")]
    public async Task<IActionResult> CreatePortalUser(SaveUserRequest request, CancellationToken ct)
    {
        if (!request.Password.Any(char.IsUpper)
            || !request.Password.Any(char.IsLower)
            || !request.Password.Any(char.IsDigit)
            || !request.Password.Any(c => !char.IsLetterOrDigit(c)))
        {
            return ValidationFailure("password", "Use at least 12 characters including uppercase, lowercase, a number and a symbol.");
        }

        if (safety is not null)
        {
            await safety.ValidatePasswordStrength(request.Password, ct);
        }

        var id = await users.CreateUser(
            UserId(User),
            request,
            hasher.HashPassword(new PortalPasswordUser(Guid.Empty), request.Password),
            ct);
        return Created("/api/v1/operations/users/" + id, new { id });
    }

    [HttpPost("users/{id:guid}")]
    [Authorize(Policy = "users.manage")]
    public async Task<IActionResult> UpdatePortalUser(Guid id, UpdateUserRequest request, CancellationToken ct)
    {
        await users.UpdateUser(UserId(User), id, request, ct);
        return NoContent();
    }

    [HttpPost("availability")]
    [Authorize(Policy = "disputes.manage")]
    public async Task<IActionResult> Availability(AvailabilityRequest request, CancellationToken ct)
    {
        await operations.SetAvailability(UserId(User), request.Availability, ct);
        return NoContent();
    }

    [HttpGet("cases/{id:guid}")]
    [EnableRateLimiting("operations-read")]
    public async Task<IActionResult> Case(Guid id, CancellationToken ct) => Ok(await operations.GetCase(UserId(User), id, ct));

    [HttpPost("cases/{id:guid}/claim")]
    [Authorize(Policy = "disputes.manage")]
    public async Task<IActionResult> Claim(Guid id, ClaimCaseRequest request, CancellationToken ct)
    {
        await operations.ClaimCase(UserId(User), id, request, ct);
        return NoContent();
    }

    [HttpPost("cases/{id:guid}/assignment")]
    [Authorize(Policy = "operations.manage")]
    public async Task<IActionResult> Assign(Guid id, AssignmentRequest request, CancellationToken ct)
    {
        await operations.Assign(UserId(User), id, request, ct);
        return NoContent();
    }

    [HttpPost("cases/{id:guid}/unassign")]
    [Authorize(Policy = "operations.manage")]
    public async Task<IActionResult> Unassign(Guid id, UnassignCaseRequest request, CancellationToken ct)
    {
        await operations.Assign(UserId(User), id, new AssignmentRequest(request.ExpectedVersion, null, request.Reason), ct);
        return NoContent();
    }

    [HttpPost("cases/{id:guid}/deadlines")]
    [Authorize(Policy = "operations.manage")]
    public async Task<IActionResult> Deadlines(Guid id, UpdateCaseDeadlinesRequest request, CancellationToken ct)
    {
        await operations.UpdateCaseDeadlines(UserId(User), id, request, ct);
        return NoContent();
    }

    [HttpPost("cases/{id:guid}/triage")]
    [Authorize(Policy = "operations.manage")]
    public async Task<IActionResult> Triage(Guid id, TriageRequest request, CancellationToken ct)
    {
        await operations.Triage(UserId(User), id, request, ct);
        return NoContent();
    }
}
