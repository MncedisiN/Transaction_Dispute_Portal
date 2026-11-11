using DisputePortal.Api.Configuration;

namespace DisputePortal.Api.Service.ExternalServices;

internal static class InfobipEmailContent
{
    public static HttpContent BuildEmailContent(InfobipOptions options, string destination, string subject, string text) =>
        JsonContent.Create(new
    {
        messages = new[] { new {
        sender = options.EmailFromAddress,
        destinations = new[] { new { to = new[] { new { destination } } } },
        content = new { subject, text }
    } }
    });
}
