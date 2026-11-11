using System.Security.Claims;
using DisputePortal.Api.Models.Exceptions;
using FluentValidation.Results;
using Microsoft.AspNetCore.Mvc;

namespace DisputePortal.Api.Controllers;

public abstract class PortalControllerBase : ControllerBase
{
    protected IActionResult ValidationFailure(ValidationResult validation, string title = "Validation failed.")
    {
        var errors = validation.Errors.GroupBy(error => string.IsNullOrWhiteSpace(error.PropertyName) ? "request" : error.PropertyName).ToDictionary(
            group => group.Key,
            group => group.Select(error => error.ErrorMessage).ToArray());
        return UnprocessableEntity(new ValidationProblemDetails(errors)
        {
            Status = 422,
            Title = title
        });
    }

    protected IActionResult ValidationFailure(string field, string message) =>
        UnprocessableEntity(new ValidationProblemDetails(new Dictionary<string, string[]> { [field] = [message] })
    {
        Status = 422,
        Title = "Validation failed."
    });

    protected Guid UserId(ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : throw new UnauthorizedAccessException("The authenticated identity has no valid user identifier.");

    protected static (int Page, int PageSize) Page(int? page, int? pageSize) =>
        (Math.Max(1, page ?? 1), Math.Clamp(pageSize ?? 20, 1, 100));

    protected static string? NormalizeFilter(string? value, params string[] allowed)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var match = allowed.FirstOrDefault(candidate => string.Equals(candidate, value.Trim(), StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            throw new PortalValidationException("The supplied filter is not supported.", "filter");
        }

        return match;
    }
}
