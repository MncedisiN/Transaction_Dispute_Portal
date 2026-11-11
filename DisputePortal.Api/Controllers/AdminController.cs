using DisputePortal.Api.Models.Internal;
using DisputePortal.Api.Models.Requests;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using DisputePortal.Api.Interfaces.Repositories;
using DisputePortal.Api.Models.Domain;
using DisputePortal.Api.Interfaces.Disputes;

namespace DisputePortal.Api.Controllers;

[ApiController]
[Route("api/v1/admin")]
[Tags("Administration")]
public class AdminController(
    IDisputeRepository repository,
    IAuditRepository audit,
    IDisputeService disputes,
    IValidator<TransitionRequest> transitionValidator,
    IValidator<AddNoteRequest> addNoteValidator) : PortalControllerBase
{
    [HttpGet("dashboard")]
    [Authorize(Policy = "disputes.read")]
    public async Task<IActionResult> Dashboard(CancellationToken cancellationToken) => Ok(await repository.GetDashboard(
        cancellationToken,
        UserId(User)));

    [HttpGet("disputes")]
    [Authorize(Policy = "disputes.read")]
    public async Task<IActionResult> Disputes(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? search,
        [FromQuery] string? status,
        [FromQuery] int? priority,
        CancellationToken cancellationToken,
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null)
    {
        var paging = Page(page, pageSize);
        var normalizedStatus = NormalizeFilter(
            status,
            "active",
            "closed",
            "Submitted",
            "UnderReview",
            "AwaitingCustomer",
            "Resolved",
            "Declined",
            "Withdrawn");
        if (priority is < 1 or > 5)
        {
            return ValidationFailure("priority", "Priority must be between 1 and 5.");
        }

        return Ok(await repository.GetAdminQueue(
            new AdminQueueFilter
            {
                Page = paging.Page,
                PageSize = paging.PageSize,
                Search = search,
                Status = normalizedStatus,
                Priority = priority,
                From = from,
                To = to
            },
            cancellationToken,
            UserId(User)));
    }

    [HttpGet("disputes/{id:guid}")]
    [Authorize(Policy = "disputes.read")]
    public async Task<IActionResult> Dispute(Guid id, CancellationToken cancellationToken)
    {
        var dispute = await repository.GetAdminDispute(id, cancellationToken, UserId(User));
        return dispute is null ? NotFound() : Ok(dispute);
    }

    [HttpPost("disputes/{id:guid}/transitions")]
    [Authorize(Policy = "disputes.manage")]
    [EnableRateLimiting("sensitive")]
    public async Task<IActionResult> Transition(Guid id, [FromBody] TransitionRequest request, CancellationToken cancellationToken)
    {
        var validation = await transitionValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return ValidationFailure(validation);
        }

        var version = await disputes.Transition(
            UserId(User),
            id,
            new TransitionCommand(request.ExpectedVersion, request.Status, request.Comment),
            HttpContext.Items["CorrelationId"]?.ToString() ?? "unknown",
            cancellationToken);
        return Ok(new { version });
    }

    [HttpPost("disputes/{id:guid}/notes")]
    [Authorize(Policy = "disputes.manage")]
    [EnableRateLimiting("sensitive")]
    public async Task<IActionResult> AddNote(Guid id, [FromBody] AddNoteRequest request, CancellationToken cancellationToken)
    {
        var validation = await addNoteValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return ValidationFailure(validation);
        }

        await disputes.AddNote(
            UserId(User),
            id,
            new NoteCommand(request.Content, request.Visibility),
            HttpContext.Items["CorrelationId"]?.ToString() ?? "unknown",
            cancellationToken);
        return NoContent();
    }

    [HttpGet("audit-logs")]
    [Authorize(Policy = "audit.read")]
    public async Task<IActionResult> AuditLogs(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? action,
        CancellationToken cancellationToken)
    {
        var paging = Page(page, pageSize);
        return Ok(await audit.GetAuditLogs(paging.Page, paging.PageSize, action, cancellationToken));
    }
}
