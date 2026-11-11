using System.Buffers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using DisputePortal.Platform.Infrastructure.Models;
using DisputePortal.Platform.Infrastructure.Interfaces;

namespace DisputePortal.Platform.Infrastructure;

public static class HttpEventEndpoint
{
    public const string Path = "/operations/events";

    public static void MapHttpEventDelivery(this IEndpointRouteBuilder endpoints, string scheme = "Bearer")
    {
        endpoints.MapPost(Path, Receive).RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = scheme }).DisableAntiforgery(); // Service JWTs only; portal cookies cannot authorize this endpoint.
    }

    private static async Task<IResult> Receive(
        HttpContext context,
        ServiceOptions options,
        IEnumerable<IDomainEventConsumer> consumers,
        CancellationToken ct)
    {
        if (options.BrokerEnabled)
        {
            return Results.Problem(statusCode: 409, title: "HTTP event transport is disabled.");
        }

        var limit = ((options.MaximumEventBytes + 2) / 3) * 4 + 2048;
        if (context.Request.ContentLength > limit)
        {
            return Results.StatusCode(413);
        }

        var feature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (feature is { IsReadOnly: false })
        {
            feature.MaxRequestBodySize = limit;
        }
         
        var buffer = ArrayPool<byte>.Shared.Rent(limit + 1);
        DisputePortal.Platform.Contracts.DomainEvent value;
        try
        {
            var length = 0;
            while (length <= limit)
            {
                var read = await context.Request.Body.ReadAsync(buffer.AsMemory(length, limit + 1 - length), ct);
                if (read == 0)
                {
                    break;
                }

                length += read;
            }

            if (length > limit)
            {
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            }

            var delivery = JsonSerializer.Deserialize<SignedEventDelivery>(
                buffer.AsSpan(0, length),
                DisputePortal.Platform.Contracts.DomainEvent.Json);
            if (delivery?.Body is null)
            {
                return Results.BadRequest();
            }

            value = EventSignature.Verify(delivery.Body, delivery.Signature, options);
        }
        catch (BadHttpRequestException error) when (error.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException or CryptographicException or ArgumentException)
        {
            return Results.Problem(statusCode: 400, title: "Invalid signed event.");
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }

        if (context.User.FindFirstValue("caller") != value.SourceService)
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var receivers = consumers.Where(c => c.Subscriptions.Any(pattern => EventRouting.Matches(pattern, value.EventType))).ToArray();
        if (receivers.Length == 0)
        {
            return Results.Problem(statusCode: 409, title: "No registered consumer accepts this event.");
        }

        using var activity = PlatformMetrics.StartEventActivity("event.consume", value, System.Diagnostics.ActivityKind.Consumer);
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var successful = false;
        try
        {
            foreach (var consumer in receivers)
            {
                await consumer.Handle(value, ct);
            }

            PlatformMetrics.RecordConsumed();
            successful = true;
            return Results.NoContent(); // ACK only after every matching consumer committed its inbox transaction.
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            PlatformMetrics.RecordFailure();
            activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error);
            throw;
        }
        finally
        {
            PlatformMetrics.RecordDuration(started, value.EventType, successful);
        }
    }
}
