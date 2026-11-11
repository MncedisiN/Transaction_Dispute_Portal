using System.Security.Claims;

namespace DisputePortal.Platform.Infrastructure.Interfaces;

public interface IServiceClient
{
    Task<T> SendServiceRequest<T>(
        string service,
        HttpMethod method,
        string path,
        object? body = null,
        ClaimsPrincipal? actor = null,
        CancellationToken ct = default);
}
