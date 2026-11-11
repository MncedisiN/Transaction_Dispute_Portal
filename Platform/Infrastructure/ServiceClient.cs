using DisputePortal.Platform.Contracts;
using System.Net.Http.Headers;
using Polly.CircuitBreaker;
using Polly.Timeout;
using Polly.RateLimiting;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using DisputePortal.Platform.Infrastructure.Interfaces;
using DisputePortal.Platform.Infrastructure.Models;

namespace DisputePortal.Platform.Infrastructure;

public class ServiceClient(HttpClient http, ServiceOptions options) : IServiceClient
{
    public async Task<T> SendServiceRequest<T>(
        string service,
        HttpMethod method,
        string path,
        object? body = null,
        ClaimsPrincipal? actor = null,
        CancellationToken ct = default)
    {
        if (!options.Endpoints.TryGetValue(service, out var endpoint))
        {
            throw new PlatformException(503, "service_unconfigured", "A required service is not configured.");
        }

        try
        {
            using var request = new HttpRequestMessage(method, new Uri(new Uri(endpoint.TrimEnd('/') + "/"), path.TrimStart('/')));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ServiceSecurity.Token(options, service, actor));
            var correlation = System.Diagnostics.Activity.Current?.GetTagItem("portal.correlation_id")
                ?? System.Diagnostics.Activity.Current?.GetTagItem("correlation.id");
            if (Guid.TryParse(correlation?.ToString(), out var correlationId))
            {
                request.Headers.Add("X-Correlation-ID", correlationId.ToString("D"));
            }
            if (body is not null)
            {
                request.Content = JsonContent.Create(body);
            }

            using var response = await http.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                ServiceProblem? error = null;
                try
                {
                    error = await response.Content.ReadFromJsonAsync<ServiceProblem>(ct);
                }
                catch (JsonException)
                {
                }

                throw new PlatformException(
                    (int)response.StatusCode,
                    error?.Code ?? "service_unavailable",
                    error?.Message ?? "A required service could not complete this request.");
            }

            if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
            {
                return default!;
            }

            var result = await response.Content.ReadFromJsonAsync<T>(ct);
            if (result is null)
            {
                throw new JsonException("A required service response was absent.");
            }

            return result;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error) when (error is BrokenCircuitException or TimeoutRejectedException or RateLimiterRejectedException)
        {
            throw new PlatformException(503, "service_unavailable", "A required service is temporarily unavailable. Its saved status will be checked before retrying.", error);
        }
        catch (JsonException error)
        {
            throw new PlatformException(503, "invalid_service_response", "A required service returned an unreadable response.", error);
        }
        catch (HttpRequestException error)
        {
            throw new PlatformException(503, "service_unavailable", "A required service is temporarily unavailable.", error);
        }
        catch (OperationCanceledException error) when (!ct.IsCancellationRequested)
        {
            throw new PlatformException(
                503,
                "service_timeout",
                "The request is taking longer than expected. Its saved status will be checked before retrying.",
                error);
        }
    }
}
