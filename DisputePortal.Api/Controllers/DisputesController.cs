using System.Security.Claims;
using DisputePortal.Api.Models.Internal;
using DisputePortal.Api.Models.Requests;
using DisputePortal.Api.Service.Disputes;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using DisputePortal.Api.Interfaces.Repositories;
using DisputePortal.Api.Models.Domain;
using DisputePortal.Api.Interfaces.Disputes;

namespace DisputePortal.Api.Controllers;

[ApiController]
[Route("api/v1/disputes")]
[Authorize(Policy = "customer")]
[Tags("Disputes")]
public class DisputesController(IDisputeRepository repository, IDisputeService disputes, IValidator<CreateDisputeRequest> validator) : PortalControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetCustomerDisputes(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] Guid? accountId,
        [FromQuery] string? status,
        [FromQuery] string? search,
        CancellationToken cancellationToken,
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null)
    {
        var paging = Page(page, pageSize);
        return Ok(await repository.GetCustomerDisputes(
            UserId(User),
            paging.Page,
            paging.PageSize,
            cancellationToken,
            accountId,
            string.IsNullOrWhiteSpace(status) ? null : status,
            search,
            new HistoryFilter
            {
                Status = null,
                From = from,
                To = to
            }));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var dispute = await repository.GetCustomerDispute(UserId(User), id, cancellationToken);
        return dispute is null ? NotFound() : Ok(dispute);
    }

    [HttpPost]
    [EnableRateLimiting("sensitive")]
    public async Task<IActionResult> SubmitCustomerDispute(
        [FromBody] CreateDisputeRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return ValidationFailure(validation);
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return ValidationFailure("Idempotency-Key", "An idempotency key is required.");
        }

        var created = await disputes.CreateCustomerDispute(
            UserId(User),
            new CreateDisputeCommand
            {
                TransactionId = request.TransactionId,
                Category = request.Category,
                Reason = request.Reason,
                Description = request.Description,
                VerificationId = request.VerificationId,
                SessionId = Guid.TryParse(User.FindFirstValue("session_id"), out var sessionId) ? sessionId : null,
                RequestSpecialistReview = request.RequestSpecialistReview
            },
            idempotencyKey,
            cancellationToken);
        return Created($"/api/v1/disputes/{created.Id}", created);
    }

    [HttpPost("{id:guid}/reinstate")]
    [EnableRateLimiting("sensitive")]
    public async Task<IActionResult> Reinstate(Guid id, [FromBody] ReinstateDisputeRequest request, CancellationToken cancellationToken)
    {
        var customer = UserId(User);
        var result = await repository.ReinstateCustomerDispute(
            customer,
            id,
            request,
            HttpContext.Items["CorrelationId"]?.ToString() ?? "unknown",
            cancellationToken);
        return Ok(result);
    }

    [HttpPost("{id:guid}/withdraw")]
    [EnableRateLimiting("sensitive")]
    public async Task<IActionResult> Withdraw(Guid id, [FromBody] WithdrawDisputeRequest request, CancellationToken cancellationToken)
    {
        var customerId = UserId(User);
        await disputes.Withdraw(
            customerId,
            id,
            new WithdrawDisputeCommand(request.ExpectedVersion),
            HttpContext.Items["CorrelationId"]?.ToString() ?? "unknown",
            cancellationToken);
        var dispute = await repository.GetCustomerDispute(customerId, id, cancellationToken);
        return dispute is null ? NotFound() : Ok(dispute);
    }
}
