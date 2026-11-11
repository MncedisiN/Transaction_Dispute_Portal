using DisputePortal.Api.Models.Internal;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DisputePortal.Api.Interfaces.Repositories;

namespace DisputePortal.Api.Controllers;

[ApiController]
[Route("api/v1/transactions")]
[Authorize(Policy = "customer")]
[Tags("Transactions")]
public class TransactionsController(ITransactionRepository repository) : PortalControllerBase
{
    [HttpGet("types")]
    public async Task<IActionResult> Types(CancellationToken ct) => Ok(await repository.GetTransactionTypes(ct));

    [HttpGet]
    public async Task<IActionResult> GetCustomerTransactions(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? search,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] string? direction,
        [FromQuery] string? type,
        [FromQuery] string? sort,
        [FromQuery] Guid? accountId,
        CancellationToken cancellationToken)
    {
        var paging = Page(page, pageSize);
        if (from.HasValue && to.HasValue && from > to)
        {
            return ValidationFailure("to", "End date must be on or after start date.");
        }

        var types = await repository.GetTransactionTypes(cancellationToken);
        var filter = new TransactionFilter
        {
            Page = paging.Page,
            PageSize = paging.PageSize,
            Search = search,
            From = from,
            To = to,
            Direction = NormalizeFilter(direction, "Debit", "Credit"),
            Type = NormalizeFilter(type, types.Select(item => item.TypeKey).ToArray()),
            Sort = NormalizeFilter(sort, "date_desc", "date_asc", "amount_desc", "amount_asc") ?? "date_desc",
            AccountId = accountId
        };
        return Ok(await repository.GetTransactions(UserId(User), filter, cancellationToken));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var item = await repository.GetTransaction(UserId(User), id, cancellationToken);
        return item is null ? NotFound() : Ok(item);
    }
}
