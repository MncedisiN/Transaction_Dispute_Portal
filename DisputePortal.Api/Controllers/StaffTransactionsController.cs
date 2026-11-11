using DisputePortal.Api.Models.Domain;
using DisputePortal.Api.Models.Requests;
using DisputePortal.Api.Service.Disputes;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using DisputePortal.Api.Interfaces.Repositories;
using DisputePortal.Api.Interfaces.Disputes;

namespace DisputePortal.Api.Controllers;

[ApiController]
[Route("api/v1/admin/transactions")]
[Authorize(Policy = "transactions.assist")]
[Tags("Customer assistance")]
public class StaffTransactionsController(
    IStaffTransactionRepository transactions,
    IDisputeService dispatcher,
    IValidator<CreateDisputeRequest> validator) : PortalControllerBase
{
    [HttpGet]
    [EnableRateLimiting("operations-read")]
    public async Task<IActionResult> Search(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? search,
        [FromQuery] string? status,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken ct)
    {
        var paging = Page(page, pageSize);
        return Ok(await transactions.Search(
            UserId(User),
            paging.Page,
            paging.PageSize,
            search,
            NormalizeFilter(status, "Posted", "Pending", "Reversed"),
            from,
            to,
            ct));
    }

    [HttpGet("{id:guid}")]
    [EnableRateLimiting("operations-read")]
    public async Task<IActionResult> Detail(Guid id, CancellationToken ct) => Ok(await transactions.GetStaffTransactionDetails(
        UserId(User),
        id,
        ct));

    [HttpPost("{id:guid}/disputes")]
    [EnableRateLimiting("sensitive")]
    public async Task<IActionResult> CreateDisputeForCustomer(
        Guid id,
        [FromBody] CreateDisputeRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? key,
        CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            return ValidationFailure(validation);
        }

        if (request.TransactionId != id)
        {
            return ValidationFailure("transactionId", "Choose the transaction being disputed.");
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            return ValidationFailure("Idempotency-Key", "A submission key is required.");
        }

        var actor = UserId(User);
        var detail = await transactions.GetStaffTransactionDetails(actor, id, ct);
        var created = await dispatcher.CreateCustomerDispute(
            detail.Transaction.CustomerId,
            new CreateDisputeCommand
            {
                TransactionId = id,
                Category = request.Category,
                Reason = request.Reason,
                Description = request.Description,
                StaffActorId = actor,
                RequestSpecialistReview = true
            },
            key,
            ct);
        return Created($"/api/v1/admin/disputes/{created.Id}", created);
    }
}
