using System.Net.Http.Headers;
using DisputePortal.Api.Configuration;
using DisputePortal.Api.Models.Exceptions;
using DisputePortal.Api.Interfaces.ExternalServices;
using DisputePortal.Api.Interfaces.Services;

namespace DisputePortal.Api.Service.ExternalServices;

public class InfobipWorkflowDelivery(HttpClient http, InfobipOptions options, IMessageTemplates? templates = null) : IWorkflowMessageDelivery
{
    public bool IsAvailable(string channel) => options.Enabled
        && Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var uri)
        && uri.Scheme == "https"
        && uri.IsDefaultPort
        && uri.Host.EndsWith(".api.infobip.com", StringComparison.OrdinalIgnoreCase)
        && string.IsNullOrEmpty(uri.UserInfo)
        && uri.AbsolutePath == "/"
        && channel switch
        {
            "sms" => !string.IsNullOrEmpty(options.SmsApiKey)
            && !string.IsNullOrEmpty(options.SmsSender),
            "email" => !string.IsNullOrEmpty(options.EmailApiKey)
            && !string.IsNullOrEmpty(options.EmailFromAddress),
            "whatsapp" => !string.IsNullOrEmpty(options.WhatsAppApiKey)
            && !string.IsNullOrEmpty(options.WhatsAppSender)
            && !string.IsNullOrEmpty(options.NotificationWhatsAppTemplateName),
            _ => false
        };

    public async Task SendImportantWorkflowMessage(string channel, string destination, string milestone, CancellationToken ct)
    {
        if (!IsAvailable(channel))
        {
            throw new VerificationUnavailableException();
        }

        var portalLink = Uri.TryCreate(options.PortalPublicUrl, UriKind.Absolute, out var portal)
            && portal.Scheme is "https" or "http"
            && string.IsNullOrEmpty(portal.UserInfo) ? new Uri(portal, "/notifications").AbsoluteUri : "";
        var rendered = (templates ?? MessageTemplateCatalog.Default).Render(
            "workflow/" + (milestone == "Started" ? "Started" : "Completed"),
            new Dictionary<string, string> { ["portalLink"] = portalLink });
        var title = rendered.Subject;
        var text = rendered.Text;
        HttpContent content;
        string path, key;
        if (channel == "sms")
        {
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
        }
        else if (channel == "email")
        {
            path = "email/4/messages";
            key = options.EmailApiKey;
            content = InfobipEmailContent.BuildEmailContent(options, destination, title, text);
        }
        else
        {
            path = "whatsapp/1/message/template";
            key = options.WhatsAppApiKey;
            content = JsonContent.Create(new
            {
                messages = new[] { new {
                from = options.WhatsAppSender.TrimStart('+'),
                to = destination.TrimStart('+'),
                messageId = Guid.NewGuid().ToString(),
                content = new {
                templateName = options.NotificationWhatsAppTemplateName,
                language = options.WhatsAppTemplateLanguage,
                templateData = new { body = new { placeholders = new[] { title } } }
            }
            } }
            });
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(options.BaseUrl.TrimEnd('/') + "/"), path));
        request.Headers.Authorization = new AuthenticationHeaderValue("App", key);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = content;
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new VerificationUnavailableException();
        }

        using var body = await System.Text.Json.JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        if (!body.RootElement.TryGetProperty("messages", out var messages) || messages.GetArrayLength() == 0)
        {
            throw new VerificationUnavailableException();
        }

        foreach (var message in messages.EnumerateArray())
            if (!message.TryGetProperty("messageId", out _)
            || (message.TryGetProperty("status", out var status)
            && status.TryGetProperty("groupId", out var group)
            && group.GetInt32() is not (1 or 3)))
            {
                throw new VerificationUnavailableException();
            }
    }
}
