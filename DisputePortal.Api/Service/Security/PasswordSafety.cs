using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using DisputePortal.Api.Models.Exceptions;
using DisputePortal.Api.Models.Domain;

namespace DisputePortal.Api.Service.Security;

public class PasswordSafety(HttpClient client, IConfiguration configuration, ILogger<PasswordSafety>? logger = null)
{
    private const int MaximumResponseBytes = 2_000_000;

    public async Task ValidatePasswordStrength(string password, CancellationToken ct)
    {
        if (!PasswordPolicy.IsStrong(password))
        {
            throw new PortalValidationException("Choose 12–128 characters with upper and lower case letters, a number and a symbol; avoid common passwords.");
        }

        if (!configuration.GetValue<bool>("PasswordSecurity:CheckBreaches"))
        {
            return;
        }

        // SHA-1 is the public range protocol only, never the stored password hash.
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(password)));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(client.Timeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, hash[..5]);
            request.Headers.Add("Add-Padding", "true");
            request.Headers.UserAgent.ParseAdd("Dispute-Password-Safety/1.0");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > MaximumResponseBytes)
            {
                throw new HttpRequestException("Oversized range response.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
            using var buffer = new MemoryStream();
            var chunk = new byte[8192];
            int read;
            while ((read = await stream.ReadAsync(chunk, deadline.Token)) > 0)
            {
                if (buffer.Length + read > MaximumResponseBytes)
                {
                    throw new HttpRequestException("Oversized range response.");
                }

                await buffer.WriteAsync(chunk.AsMemory(0, read), deadline.Token);
            }

            var lines = Encoding.ASCII.GetString(buffer.ToArray()).Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length is 0 or > 20000)
            {
                throw new HttpRequestException("Invalid range response.");
            }

            foreach (var raw in lines)
            {
                var parts = raw.TrimEnd('\r').Split(':');
                if (parts.Length != 2
                    || parts[0].Length != 35
                    || !parts[0].All(char.IsAsciiHexDigit)
                    || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var count))
                {
                    throw new HttpRequestException("Invalid range response.");
                }

                if (count > 0 && parts[0].Equals(hash[5..], StringComparison.OrdinalIgnoreCase))
                {
                    throw new PortalValidationException("This password appears in known breaches. Choose a different password.");
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException || ex is OperationCanceledException && !ct.IsCancellationRequested)
        {
            logger?.LogWarning("Password safety service unavailable {FailureType}", ex.GetType().Name);
            throw new PasswordSafetyUnavailableException();
        }
    }
}
