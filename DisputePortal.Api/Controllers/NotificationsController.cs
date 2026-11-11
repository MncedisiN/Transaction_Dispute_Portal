using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DisputePortal.Api.Interfaces.Repositories;

namespace DisputePortal.Api.Controllers;

[ApiController]
[Route("api/v1/notifications")]
[Authorize]
[Tags("Notifications")]
public class NotificationsController(INotificationRepository repository) : PortalControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetCustomerNotifications(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken)
    {
        var paging = Page(page, pageSize);
        return Ok(await repository.GetNotifications(UserId(User), paging.Page, paging.PageSize, cancellationToken));
    }

    [HttpGet("unread-count")]
    public async Task<IActionResult> UnreadCount(CancellationToken ct) => Ok(new
    {
        count = await repository.GetUnreadNotificationCount(UserId(User), ct)
    });

    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken cancellationToken) => await repository.MarkNotificationRead(
        UserId(User),
        id,
        cancellationToken) ? NoContent() : NotFound();
}
