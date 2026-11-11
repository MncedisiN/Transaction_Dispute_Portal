namespace DisputePortal.Api.Interfaces.Security;

public interface IRegistrationSessionIssuer
{
    Task Issue(Guid userId, HttpContext context, CancellationToken ct);
}
