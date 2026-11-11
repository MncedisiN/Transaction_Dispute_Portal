using DisputePortal.Api.Models.Security;
using DisputePortal.Api.Models.Domain;
using DisputePortal.Api.Interfaces.ExternalServices;

namespace DisputePortal.Api.Service.ExternalServices;

public static class VerificationMessage
{
    public static int GetRemainingCodeMinutes(DateTimeOffset expiresAt) =>
        Math.Clamp((int)Math.Ceiling((expiresAt - DateTimeOffset.UtcNow).TotalMinutes), 1, 15);

    public static RenderedMessage BuildVerificationMessage(
        VerificationPurpose purpose,
        string code,
        int minutes,
        IMessageTemplates? templates = null)
    {
        if (!Enum.IsDefined(purpose) || code is not { Length: 6 } || code.Any(c => c is < '0' or > '9') || minutes is < 1 or > 15)
        {
            throw new ArgumentException("Use a supported purpose, a six-digit code and a valid expiry.");
        }

        return (templates ?? MessageTemplateCatalog.Default).Render(
            "verification/" + purpose,
            new Dictionary<string, string>
            {
                ["code"] = code,
                ["minutes"] = minutes.ToString(System.Globalization.CultureInfo.InvariantCulture)
            });
    }
}
