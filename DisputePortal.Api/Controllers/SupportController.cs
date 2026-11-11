using DisputePortal.Api.Models.Internal;
using DisputePortal.Api.Models.Requests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using DisputePortal.Api.Interfaces.Repositories;

namespace DisputePortal.Api.Controllers;

[ApiController]
[Route("api/v1/support")]
[Authorize]
public class SupportController(ISupportRepository operations, ISupportReadRepository readState) : PortalControllerBase
{
    [HttpGet("concerns")]
    public async Task<IActionResult> Concerns(CancellationToken ct) => Ok(await operations.GetConcerns(ct));

    [HttpPost("{id:guid}/close")]
    [EnableRateLimiting("sensitive")]
    public async Task<IActionResult> Close(Guid id, CloseSupportRequest request, CancellationToken ct) =>
        Ok(await operations.CloseConversation(UserId(User), id, request, ct));

    [HttpPost("{id:guid}/assign")]
    [Authorize(Policy = "operations.manage")]
    [EnableRateLimiting("sensitive")]
    public async Task<IActionResult> Assign(Guid id, AssignmentRequest request, CancellationToken ct) =>
        Ok(await operations.AssignConversation(UserId(User), id, request, ct));

    [HttpPost("{id:guid}/escalate")]
    [Authorize(Policy = "disputes.manage")]
    [EnableRateLimiting("sensitive")]
    public async Task<IActionResult> Escalate(Guid id, EscalationRequest request, CancellationToken ct) =>
        Ok(await operations.EscalateConversation(UserId(User), id, request, ct));

    [HttpGet("related-cases")]
    public async Task<IActionResult> RelatedCases(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? reference,
        CancellationToken ct)
    {
        var paging = Page(page, pageSize);
        return Ok(await operations.GetRelatedCases(UserId(User), paging.Page, Math.Min(paging.PageSize, 50), reference, ct));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Thread(Guid id, CancellationToken ct)
    {
        var thread = await readState.GetSupportThread(UserId(User), id, ct);
        return thread is null ? NotFound() : Ok(thread);
    }

    [HttpGet("last-opened")]
    public async Task<IActionResult> LastOpened(CancellationToken ct) => Ok(new
    {
        threadId = await readState.GetLastSupportThread(UserId(User), ct)
    });

    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> MarkConversationRead(Guid id, SupportReadRequest request, CancellationToken ct)
    {
        await readState.MarkSupportRead(UserId(User), id, request.MessageId, ct);
        return NoContent();
    }

    [HttpGet]
    public async Task<IActionResult> Threads(
        [FromQuery] Guid? disputeId,
        CancellationToken ct,
        [FromQuery] string? search = null,
        [FromQuery] Guid? transactionId = null,
        [FromQuery] string? status = null,
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        [FromQuery] bool unreadOnly = false,
        [FromQuery] Guid? relatedDisputeId = null,
        [FromQuery] int? page = null,
        [FromQuery] int? pageSize = null,
        [FromQuery] bool assignedOnly = false,
        [FromQuery] bool ownOnly = false)
    {
        var paging = Page(page, pageSize);
        var result = await operations.GetThreads(
            UserId(User),
            ct,
            disputeId,
            search,
            transactionId,
            new HistoryFilter
            {
                Status = status,
                From = from,
                To = to,
                UnreadOnly = unreadOnly
            },
            relatedDisputeId,
            paging.Page,
            paging.PageSize,
            assignedOnly,
            ownOnly); 
        Response.Headers["X-Total-Count"] = result.Total.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Response.Headers["X-Page"] = result.Page.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Response.Headers["X-Page-Size"] = result.PageSize.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return Ok(result.Items);
    }

    [HttpGet("{id:guid}/messages")]
    public async Task<IActionResult> Messages(
        Guid id,
        CancellationToken ct,
        [FromQuery] long? afterSequence = null,
        [FromQuery] long? beforeSequence = null)
    {
        var result = await operations.GetMessagePage(UserId(User), id, ct, afterSequence, beforeSequence); 
        Response.Headers["X-Has-More"] = result.HasMore ? "true" : "false";
        return Ok(result.Items);
    }

    [HttpPost]
    [EnableRateLimiting("sensitive")]
    public async Task<IActionResult> SendSupportMessage(SupportSendRequest request, CancellationToken ct) => Ok(new
    {
        id = await operations.SendMessage(UserId(User), request, ct)
    });
}
