namespace DisputePortal.Api.Interfaces.Services;

public interface IWorkflowMessageDelivery
{
    bool IsAvailable(string channel);

    Task SendImportantWorkflowMessage(string channel, string destination, string milestone, CancellationToken ct);
}
