using System.Net;
using DisputePortal.Api.Configuration;
using DisputePortal.Api.Service.Security;
using DisputePortal.Api.Models.Internal;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;
using DisputePortal.Api.Interfaces.Push;
using DisputePortal.Api.Interfaces.Security;

namespace DisputePortal.Api.Service.Push;

public sealed class PushTransport(HttpClient http, PushOptions options, IPushEndpointPolicy? endpointPolicy = null) : IPushTransport, IDisposable
{
    public async Task<PushAttempt> SendEncryptedWebPush(StoredPushSubscription subscription, string payload, CancellationToken ct)
    {
        if (!(endpointPolicy?.IsAllowed(subscription.Endpoint) ?? PushEndpointPolicy.IsAllowed(subscription.Endpoint)))
        {
            return PushAttempt.Gone;
        }

        try
        {
            using var authentication = new VapidAuthentication(options.PublicKey, options.PrivateKey)
            {
                Subject = options.Subject
            };
            var client = new PushServiceClient(http)
            {
                AutoRetryAfter = false,
                DefaultTimeToLive = 3600
            };
            var target = new PushSubscription
            {
                Endpoint = subscription.Endpoint,
                Keys = new Dictionary<string, string>
                {
                    ["p256dh"] = subscription.P256dh,
                    ["auth"] = subscription.Auth
                }
            };
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(http.Timeout);
            using var instrumentation = OpenTelemetry.SuppressInstrumentationScope.Begin();
            await client.RequestPushMessageDeliveryAsync(target, new PushMessage(payload), authentication, timeout.Token);
            return PushAttempt.Accepted;
        }
        catch (PushServiceClientException error) when (error.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
        {
            return PushAttempt.Gone;
        }
        catch (PushServiceClientException)
        {
            return PushAttempt.Rejected;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error) when (error is System.Security.Cryptography.CryptographicException or FormatException or ArgumentException)
        {
            return PushAttempt.Gone;
        }
        catch (Exception)
        {
            return PushAttempt.Ambiguous;
        }
    }

    public void Dispose() => http.Dispose();
}
