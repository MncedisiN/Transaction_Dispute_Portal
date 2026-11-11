using DisputePortal.Api.Models.Security;
using System.Net.Http.Headers;
using System.Text.Json;
using DisputePortal.Api.Configuration;
using DisputePortal.Api.Models.Exceptions;
using DisputePortal.Api.Interfaces.ExternalServices;
using DisputePortal.Api.Interfaces.Security;

namespace DisputePortal.Api.Service.ExternalServices;
// Delivery contract adapted from LiftApi's InfobipMessagingClient.
// Keys belong to individual channels and are attached only to that request.
public class InfobipVerificationDelivery(
    HttpClient http,
    InfobipOptions options,
    ILogger<InfobipVerificationDelivery> logger,
    IMessageTemplates? templates = null) : IVerificationDelivery
{
    private bool HasTrustedEndpoint => Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var uri)
        && uri.Scheme == "https"
        && uri.Host.EndsWith(".api.infobip.com", StringComparison.OrdinalIgnoreCase)
        && uri.IsDefaultPort
        && string.IsNullOrEmpty(uri.UserInfo)
        && uri.AbsolutePath == "/";

    public bool IsAvailable(string channel) => options.Enabled
        && HasTrustedEndpoint
        && options.OtpHashKey.Length >= 32
        && channel switch
        {
            "sms" => !string.IsNullOrWhiteSpace(options.SmsApiKey)
            && !string.IsNullOrWhiteSpace(options.SmsSender),
            "email" => !string.IsNullOrWhiteSpace(options.EmailApiKey)
            && !string.IsNullOrWhiteSpace(options.EmailFromAddress),
            "whatsapp" => !string.IsNullOrWhiteSpace(options.WhatsAppApiKey)
            && !string.IsNullOrWhiteSpace(options.WhatsAppSender)
            && !string.IsNullOrWhiteSpace(options.WhatsAppTemplateName)
            && !string.IsNullOrWhiteSpace(options.WhatsAppTemplateLanguage),
            _ => false
        };

    public async Task SendVerificationCode(
        string channel,
        string destination,
        string code,
        int minutes,
        VerificationPurpose purpose,
        CancellationToken ct)
    {
        if (!IsAvailable(channel))
        {
            throw new VerificationUnavailableException();
        }

        var verificationMessage = VerificationMessage.BuildVerificationMessage(purpose, code, minutes, templates);
        var text = verificationMessage.Text;
        string path, key;
        HttpContent content;
        switch (channel)
        {
            case "sms":
                path = "sms/3/messages";
                key = options.SmsApiKey;
                content = JsonContent.Create(new
                {
                    messages = new[] { new {
            sender = options.SmsSender,
            destinations = new[] { new { to = destination.TrimStart('+') } },
            content = new { text }
        } }
                });
                break;
            case "email":
                path = "email/4/messages";
                key = options.EmailApiKey;
                content = InfobipEmailContent.BuildEmailContent(options, destination, verificationMessage.Subject, text);
                break;
            case "whatsapp":
                path = "whatsapp/1/message/template";
                key = options.WhatsAppApiKey;
                var templateData = new Dictionary<string, object>
                {
                    ["body"] = new
                    {
                        placeholders = options.WhatsAppTemplateCodeOnly ? new[]
                        {
                            code
                        }

                        : options.WhatsAppTemplateIncludesAction ? new[]
                        {
                            verificationMessage.Action,
                            code,
                            minutes.ToString(System.Globalization.CultureInfo.InvariantCulture)
                        }

                        : new[]
                        {
                            text
                        }
                    }
                };
                if (options.WhatsAppTemplateUsesOtpButton)
                {
                    templateData["buttons"] = new[]
                    {
                        new
                        {
                type = "URL",
                parameter = code
            }
                    };
                }

                content = JsonContent.Create(new
                {
                    messages = new[] { new {
            from = options.WhatsAppSender.TrimStart('+'),
            to = destination.TrimStart('+'),
            messageId = Guid.NewGuid().ToString(),
            content = new {
            templateName = options.WhatsAppTemplateName,
            language = options.WhatsAppTemplateLanguage,
            templateData
        }
        } }
                });
                break;
            default:
                throw new VerificationUnavailableException();
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(options.BaseUrl.TrimEnd('/') + "/"), path));
        request.Headers.Authorization = new AuthenticationHeaderValue("App", key);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = content;
        try
        {
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Verification delivery rejected. Channel {Channel}, HTTP {Status}", channel, (int)response.StatusCode);
                throw new VerificationUnavailableException();
            }

            using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            if (!body.RootElement.TryGetProperty("messages", out var messages)
                || messages.ValueKind != JsonValueKind.Array
                || messages.GetArrayLength() == 0)
            {
                throw new VerificationUnavailableException();
            }

            foreach (var message in messages.EnumerateArray())
            {
                if (!message.TryGetProperty("messageId", out var id)
                    || id.ValueKind != JsonValueKind.String
                    || string.IsNullOrWhiteSpace(id.GetString()))
                {
                    throw new VerificationUnavailableException();
                }

                if (!message.TryGetProperty("status", out var status)
                    || status.ValueKind != JsonValueKind.Object
                    || !status.TryGetProperty("groupId", out var group)
                    || group.ValueKind != JsonValueKind.Number
                    || !group.TryGetInt32(out var groupId))
                {
                    throw new VerificationUnavailableException();
                }

                var providerStatus = status.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String ? SafeStatus(name.GetString()) : "UNSPECIFIED";
                if (groupId is not (1 or 3))
                {
                    logger.LogWarning(
                        "Verification provider rejected message. Channel {Channel}, ProviderStatus {ProviderStatus}, Group {Group}",
                        channel,
                        providerStatus,
                        groupId);
                    throw new VerificationUnavailableException();
                }

                // Accepted/queued is not the same as delivered. Never log the destination, code or payload.
                logger.LogInformation(
                    "Verification provider accepted message. Channel {Channel}, ProviderStatus {ProviderStatus}, Group {Group}, MessageId {MessageId}",
                    channel,
                    providerStatus,
                    groupId,
                    SafeStatus(id.GetString()));
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new VerificationUnavailableException();
        }
        catch (HttpRequestException)
        {
            throw new VerificationUnavailableException();
        }
        catch (JsonException)
        {
            throw new VerificationUnavailableException();
        }
    }

    private static string SafeStatus(string? value) =>
        new((value ?? "UNSPECIFIED").Where(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-').Take(80).ToArray());
}
