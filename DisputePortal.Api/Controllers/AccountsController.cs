using DisputePortal.Api.Service.Banking;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using DisputePortal.Api.Interfaces.Repositories;

namespace DisputePortal.Api.Controllers;

[ApiController]
[Route("api/v1/accounts")]
[Authorize(Policy = "customer")]
[Tags("Accounts")]
public class AccountsController(ITransactionRepository repository, BankFeedService banking) : PortalControllerBase
{
    [HttpGet("{id:guid}/connection")]
    public async Task<IActionResult> Connection(Guid id, CancellationToken ct) => Ok(await banking.GetStatus(UserId(User), id, ct));

    [HttpPost("{id:guid}/refresh")]
    [EnableRateLimiting("sensitive")]
    public async Task<IActionResult> Refresh(Guid id, CancellationToken ct) => Ok(await banking.Refresh(UserId(User), id, ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Details(Guid id, CancellationToken ct)
    {
        var account = await repository.GetAccountByPublicId(UserId(User), id, ct);
        return account is null ? NotFound() : Ok(account);
    }

    [HttpGet]
    public async Task<IActionResult> GetCustomerAccounts(CancellationToken cancellationToken) =>
        Ok(await repository.GetAccounts(UserId(User), cancellationToken));
}
