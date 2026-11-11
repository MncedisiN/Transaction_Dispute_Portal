using DisputePortal.Api.Models.Domain;

namespace DisputePortal.Api.Interfaces.ExternalServices;

public interface IMessageTemplates
{
    RenderedMessage Render(string id, IReadOnlyDictionary<string, string> variables);
}
